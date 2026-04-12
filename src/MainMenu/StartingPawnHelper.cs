using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public enum PawnNodeType
    {
        GroupHeader,
        Pawn,
        Category,
        Leaf
    }

    public enum PawnCategoryType
    {
        Bio,
        Traits,
        IncapableOf,
        Skills,
        Health,
        Possessions
    }

    /// <summary>
    /// Holds pawn-specific tree metadata stored in InspectionTreeItem.Data.
    /// </summary>
    public class PawnTreeData
    {
        public PawnNodeType NodeType { get; set; }
        public int PawnIndex { get; set; } = -1;
        public PawnCategoryType? CategoryType { get; set; }
        /// <summary>
        /// Domain-specific data (Pawn, Trait, SkillRecord, Hediff, ThingDefCount, XenotypeDef, WorkTags, etc.)
        /// </summary>
        public object DomainData { get; set; }
    }

    public struct TreePosition
    {
        public PawnCategoryType? Category;
        public int ItemIndex;
        public bool WasExpanded;
        public bool WasCategoryExpanded;

        public static readonly TreePosition Default = new TreePosition
        {
            Category = null,
            ItemIndex = -1,
            WasExpanded = false,
            WasCategoryExpanded = false
        };
    }

    public static class StartingPawnHelper
    {
        /// <summary>
        /// Builds the pawn tree as InspectionTreeItem nodes under a hidden root.
        /// The root is hidden (SkipRootInVisibleList = true in TreeNavigationHelper).
        /// </summary>
        public static InspectionTreeItem BuildTree()
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;

            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Data = new PawnTreeData { NodeType = PawnNodeType.GroupHeader }
            };

            // Wanderer mode: all pawns are selected, no group headers
            if (StartingPawnState.Context == PawnEditorContext.Wanderer)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawnNode = BuildPawnNode(pawns[i], i, root);
                    root.Children.Add(pawnNode);
                }
                return root;
            }

            // Game-start mode: pawns tagged with section name (Selected / Left Behind).
            // Sections are announced on crossing (like Dialog_InfoCard) rather than
            // occupying their own navigation row.
            string selectedSection = "StartingPawnsSelected".Translate();
            string leftBehindSection = "StartingPawnsLeftBehind".Translate();

            for (int i = 0; i < startingCount && i < pawns.Count; i++)
            {
                var pawnNode = BuildPawnNode(pawns[i], i, root);
                pawnNode.Description = selectedSection;
                root.Children.Add(pawnNode);
            }

            for (int i = startingCount; i < pawns.Count; i++)
            {
                var pawnNode = BuildPawnNode(pawns[i], i, root);
                pawnNode.Description = leftBehindSection;
                root.Children.Add(pawnNode);
            }

            return root;
        }

        private static InspectionTreeItem BuildPawnNode(Pawn pawn, int index, InspectionTreeItem parent)
        {
            string nick = pawn.Name is NameTriple nameTriple
                ? (string.IsNullOrEmpty(nameTriple.Nick) ? nameTriple.First : nameTriple.Nick)
                : pawn.LabelShort;
            string title = pawn.story?.TitleCap;

            string label = string.IsNullOrEmpty(title) ? nick : $"{nick}, {title}";

            var pawnNode = new InspectionTreeItem
            {
                Label = label,
                IndentLevel = 1,
                Type = InspectionTreeItem.ItemType.Object,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Pawn,
                    PawnIndex = index,
                    DomainData = pawn
                }
            };

            BuildPawnCategories(pawn, index, pawnNode);
            return pawnNode;
        }

        private static void BuildPawnCategories(Pawn pawn, int pawnIndex, InspectionTreeItem pawnNode)
        {
            pawnNode.Children.Clear();

            // Bio
            var bioNode = new InspectionTreeItem
            {
                Label = "Bio",
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Bio,
                    PawnIndex = pawnIndex
                }
            };
            BuildBioItems(pawn, pawnIndex, bioNode);
            pawnNode.Children.Add(bioNode);

            // Traits
            var traitsNode = new InspectionTreeItem
            {
                Label = "Traits".Translate(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Traits,
                    PawnIndex = pawnIndex
                }
            };
            BuildTraitItems(pawn, traitsNode);
            CollapseIfEmpty(traitsNode);
            pawnNode.Children.Add(traitsNode);

            // Incapable of
            var incapableNode = new InspectionTreeItem
            {
                Label = "IncapableOf".Translate(pawn),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.IncapableOf,
                    PawnIndex = pawnIndex
                }
            };
            BuildIncapableOfItems(pawn, incapableNode);
            CollapseIfEmpty(incapableNode);
            pawnNode.Children.Add(incapableNode);

            // Skills
            var skillsNode = new InspectionTreeItem
            {
                Label = "Skills".Translate(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Skills,
                    PawnIndex = pawnIndex
                }
            };
            BuildSkillItems(pawn, skillsNode);
            pawnNode.Children.Add(skillsNode);

            // Health
            var healthNode = new InspectionTreeItem
            {
                Label = "Health".Translate(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Health,
                    PawnIndex = pawnIndex
                }
            };
            BuildHealthItems(pawn, healthNode);
            CollapseIfEmpty(healthNode);
            pawnNode.Children.Add(healthNode);

            // Possessions
            var possessionsNode = new InspectionTreeItem
            {
                Label = "Possessions".Translate(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Possessions,
                    PawnIndex = pawnIndex
                }
            };
            BuildPossessionItems(pawn, pawnIndex, possessionsNode);
            CollapseIfEmpty(possessionsNode);
            pawnNode.Children.Add(possessionsNode);
        }

        /// <summary>
        /// If a category has only a single "None" child, make it non-expandable
        /// and fold "none" into the category label.
        /// </summary>
        private static void CollapseIfEmpty(InspectionTreeItem categoryNode)
        {
            if (categoryNode.Children.Count == 1
                && categoryNode.Children[0].Label == "None".Translate())
            {
                categoryNode.IsExpandable = false;
                categoryNode.Label += ": " + "None".Translate();
                categoryNode.Children.Clear();
            }
        }

        private static InspectionTreeItem MakeLeaf(string label, int indentLevel, PawnCategoryType category, int pawnIndex, InspectionTreeItem parent, string tooltip = null, object domainData = null)
        {
            return new InspectionTreeItem
            {
                Label = label,
                Tooltip = tooltip,
                IndentLevel = indentLevel,
                Type = InspectionTreeItem.ItemType.Item,
                Parent = parent,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = category,
                    PawnIndex = pawnIndex,
                    DomainData = domainData
                }
            };
        }

        private static void BuildBioItems(Pawn pawn, int pawnIndex, InspectionTreeItem bioNode)
        {
            bioNode.Children.Clear();

            // Gender and age
            string genderAge = pawn.MainDesc(writeFaction: false);
            bioNode.Children.Add(MakeLeaf(
                genderAge.CapitalizeFirst(), 3, PawnCategoryType.Bio, pawnIndex, bioNode,
                tooltip: pawn.ageTracker?.AgeTooltipString));

            // Childhood backstory
            if (pawn.story != null)
            {
                var childhood = pawn.story.GetBackstory(BackstorySlot.Childhood);
                if (childhood != null)
                {
                    bioNode.Children.Add(MakeLeaf(
                        "Childhood".Translate() + ": " + childhood.TitleCapFor(pawn.gender),
                        3, PawnCategoryType.Bio, pawnIndex, bioNode,
                        tooltip: childhood.FullDescriptionFor(pawn).Resolve()));
                }

                // Adulthood backstory
                var adulthood = pawn.story.GetBackstory(BackstorySlot.Adulthood);
                if (adulthood != null)
                {
                    bioNode.Children.Add(MakeLeaf(
                        "Adulthood".Translate() + ": " + adulthood.TitleCapFor(pawn.gender),
                        3, PawnCategoryType.Bio, pawnIndex, bioNode,
                        tooltip: adulthood.FullDescriptionFor(pawn).Resolve()));
                }

                // Custom title
                if (pawn.story.title != null)
                {
                    bioNode.Children.Add(MakeLeaf(
                        "BackstoryTitle".Translate() + ": " + pawn.story.title,
                        3, PawnCategoryType.Bio, pawnIndex, bioNode));
                }
            }

            // Xenotype (Biotech)
            if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.GenesListForReading.Any())
            {
                string xenotypeLabel = pawn.genes.XenotypeLabelCap;
                string xenotypeDesc = pawn.genes.XenotypeDescShort;
                bioNode.Children.Add(MakeLeaf(
                    "Xenotype".Translate() + ": " + xenotypeLabel,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: xenotypeDesc, domainData: pawn.genes?.Xenotype));
            }

            // Faction
            if (pawn.Faction != null && !pawn.Faction.Hidden)
            {
                bioNode.Children.Add(MakeLeaf(
                    "Faction".Translate() + ": " + pawn.Faction.Name,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode));
            }

            // Ideology
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && pawn.Ideo != null)
            {
                bioNode.Children.Add(MakeLeaf(
                    pawn.Ideo.name,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode));

                // Role
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    bioNode.Children.Add(MakeLeaf(
                        role.LabelForPawn(pawn),
                        3, PawnCategoryType.Bio, pawnIndex, bioNode,
                        tooltip: role.GetTip()));
                }
            }

            // Favorite color
            if (pawn.story?.favoriteColor != null)
            {
                string orIdeoColor = string.Empty;
                if (pawn.Ideo != null && !pawn.Ideo.classicMode)
                {
                    orIdeoColor = "OrIdeoColor".Translate(pawn.Named("PAWN"));
                }
                string colorLabel = "FavoriteColorTooltip".Translate(
                    pawn.Named("PAWN"),
                    pawn.story.favoriteColor.label.Named("COLOR"),
                    0.6f.ToStringPercent().Named("PERCENTAGE"),
                    orIdeoColor.Named("ORIDEO")
                ).Resolve();
                bioNode.Children.Add(MakeLeaf(
                    colorLabel, 3, PawnCategoryType.Bio, pawnIndex, bioNode));
            }
        }

        private static void BuildTraitItems(Pawn pawn, InspectionTreeItem traitsNode)
        {
            traitsNode.Children.Clear();
            var ptd = (PawnTreeData)traitsNode.Data;

            if (pawn.story?.traits == null || pawn.story.traits.allTraits.Count == 0)
            {
                string noTraitsLabel = pawn.DevelopmentalStage.Baby()
                    ? "TraitsDevelopLaterBaby".Translate()
                    : "None".Translate();
                traitsNode.Children.Add(MakeLeaf(
                    noTraitsLabel, 3, PawnCategoryType.Traits, ptd.PawnIndex, traitsNode));
                return;
            }

            foreach (var trait in pawn.story.traits.TraitsSorted)
            {
                string label = trait.LabelCap;
                if (trait.Suppressed)
                    label += " (" + "Suppressed".Translate() + ")";

                traitsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Traits, ptd.PawnIndex, traitsNode,
                    tooltip: trait.TipString(pawn), domainData: trait));
            }
        }

        private static void BuildIncapableOfItems(Pawn pawn, InspectionTreeItem incapableNode)
        {
            incapableNode.Children.Clear();
            var ptd = (PawnTreeData)incapableNode.Data;

            WorkTags disabledTags = pawn.CombinedDisabledWorkTags;
            if (disabledTags == WorkTags.None)
            {
                incapableNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.IncapableOf, ptd.PawnIndex, incapableNode));
                return;
            }

            var tagsList = GetWorkTagsList(disabledTags);

            foreach (var tag in tagsList)
            {
                string tagLabel = tag.LabelTranslated().CapitalizeFirst();
                string tooltip = GetWorkTypeDisabledCausedBy(pawn, tag);

                incapableNode.Children.Add(MakeLeaf(
                    tagLabel, 3, PawnCategoryType.IncapableOf, ptd.PawnIndex, incapableNode,
                    tooltip: tooltip, domainData: tag));
            }
        }

        private static List<WorkTags> GetWorkTagsList(WorkTags tags)
        {
            var list = new List<WorkTags>();
            foreach (var tag in tags.GetAllSelectedItems<WorkTags>())
            {
                if (tag != WorkTags.None)
                    list.Add(tag);
            }
            return list;
        }

        /// <summary>
        /// Returns a short single-line cause for why a work tag is disabled,
        /// e.g. "Childhood: Pacifist" or "Trait: Bloodlust".
        /// </summary>
        private static string GetFirstDisableCause(Pawn pawn, WorkTags workTag)
        {
            if (pawn.story?.Childhood != null && (pawn.story.Childhood.workDisables & workTag) != WorkTags.None)
                return "Childhood".Translate() + ": " + pawn.story.Childhood.TitleFor(pawn.gender).CapitalizeFirst();
            if (pawn.story?.Adulthood != null && (pawn.story.Adulthood.workDisables & workTag) != WorkTags.None)
                return "Adulthood".Translate() + ": " + pawn.story.Adulthood.TitleFor(pawn.gender).CapitalizeFirst();
            if (pawn.story?.traits != null)
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    if (!trait.Suppressed && (trait.def.disabledWorkTags & workTag) != WorkTags.None)
                        return "Trait".Translate() + ": " + trait.LabelCap;
                }
            }
            if (pawn.health?.hediffSet != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    var stage = hediff.CurStage;
                    if (stage != null && (stage.disabledWorkTags & workTag) != WorkTags.None)
                        return hediff.LabelCap;
                }
            }
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene.Active && (gene.def.disabledWorkTags & workTag) != WorkTags.None)
                        return gene.LabelCap;
                }
            }
            return null;
        }

        private static string GetWorkTypeDisabledCausedBy(Pawn pawn, WorkTags workTag)
        {
            var sb = new StringBuilder();

            // Check backstories
            if (pawn.story?.Childhood != null && (pawn.story.Childhood.workDisables & workTag) != WorkTags.None)
                sb.AppendLine("IncapableOfTooltipBackstory".Translate() + ": " + pawn.story.Childhood.TitleFor(pawn.gender).CapitalizeFirst());
            if (pawn.story?.Adulthood != null && (pawn.story.Adulthood.workDisables & workTag) != WorkTags.None)
                sb.AppendLine("IncapableOfTooltipBackstory".Translate() + ": " + pawn.story.Adulthood.TitleFor(pawn.gender).CapitalizeFirst());

            // Check traits
            if (pawn.story?.traits != null)
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    if (!trait.Suppressed && (trait.def.disabledWorkTags & workTag) != WorkTags.None)
                        sb.AppendLine("IncapableOfTooltipTrait".Translate() + ": " + trait.LabelCap);
                }
            }

            // Check hediffs
            if (pawn.health?.hediffSet != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    var stage = hediff.CurStage;
                    if (stage != null && (stage.disabledWorkTags & workTag) != WorkTags.None)
                        sb.AppendLine("IncapableOfTooltipHediff".Translate() + ": " + hediff.LabelCap);
                }
            }

            // Check genes (Biotech)
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene.Active && (gene.def.disabledWorkTags & workTag) != WorkTags.None)
                        sb.AppendLine("IncapableOfTooltipGene".Translate() + ": " + gene.LabelCap);
                }
            }

            // Add affected work types
            sb.AppendLine();
            sb.AppendLine("IncapableOfTooltipWorkTypes".Translate());
            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefs)
            {
                if ((workType.workTags & workTag) > WorkTags.None)
                    sb.AppendLine("- " + workType.pawnLabel);
            }

            return sb.ToString().TrimEnd();
        }

        private static void BuildSkillItems(Pawn pawn, InspectionTreeItem skillsNode)
        {
            skillsNode.Children.Clear();
            var ptd = (PawnTreeData)skillsNode.Data;

            if (pawn.DevelopmentalStage.Baby())
            {
                skillsNode.Children.Add(MakeLeaf(
                    "SkillsDevelopLaterBaby".Translate(), 3, PawnCategoryType.Skills, ptd.PawnIndex, skillsNode));
                return;
            }

            // Use game's fixed display order (listOrder descending)
            var skills = pawn.skills?.skills?
                .OrderByDescending(s => s.def.listOrder)
                .ToList();

            if (skills == null) return;

            foreach (var skill in skills)
            {
                string levelStr = skill.TotallyDisabled ? "-" : skill.Level.ToString();
                string passionStr = GetPassionLabel(skill.passion);
                string label = $"{skill.def.skillLabel.CapitalizeFirst()}: {levelStr}";
                if (!string.IsNullOrEmpty(passionStr))
                    label += $", {passionStr}";
                if (skill.TotallyDisabled)
                    label += $" ({"DisabledLower".Translate()})";

                string tooltip = BuildSkillTooltip(pawn, skill);

                skillsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Skills, ptd.PawnIndex, skillsNode,
                    tooltip: tooltip, domainData: skill));
            }
        }

        private static string GetPassionLabel(Passion passion)
        {
            switch (passion)
            {
                case Passion.Minor:
                    return "PassionMinor".Translate();
                case Passion.Major:
                    return "PassionMajor".Translate();
                default:
                    return "";
            }
        }

        /// <summary>
        /// Gets a brief summary of a pawn for reroll announcements.
        /// Includes age, traits (names only), and top 3 skills with passion levels.
        /// </summary>
        public static string GetPawnRollSummary(Pawn pawn)
        {
            if (pawn == null) return "";

            var parts = new List<string>();

            // Age
            parts.Add($"{"Stat_Age_Label".Translate()}: {pawn.ageTracker.AgeBiologicalYears}");

            // Traits (names only, no descriptions)
            if (pawn.story?.traits?.allTraits != null && pawn.story.traits.allTraits.Count > 0)
            {
                var traitNames = pawn.story.traits.allTraits
                    .Select(t => t.LabelCap)
                    .ToList();
                parts.Add($"{"Traits".Translate()}: {string.Join(", ", traitNames)}");
            }
            else
            {
                parts.Add($"{"Traits".Translate()}: {"None".Translate()}");
            }

            // Top 3 skills (excluding TotallyDisabled, sorted by level descending)
            if (pawn.skills?.skills != null)
            {
                var topSkills = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .Take(3)
                    .Select(s =>
                    {
                        string passion = GetPassionLabel(s.passion);
                        string passionSuffix = !string.IsNullOrEmpty(passion) ? $" ({passion})" : "";
                        return $"{s.def.skillLabel.CapitalizeFirst()}{passionSuffix}: {s.Level}";
                    })
                    .ToList();

                if (topSkills.Count > 0)
                    parts.Add($"{"Skills".Translate()}: {string.Join(", ", topSkills)}");
            }

            return string.Join(". ", parts);
        }

        private static string BuildSkillTooltip(Pawn pawn, SkillRecord skill)
        {
            var sb = new StringBuilder();
            sb.Append(skill.def.description);

            if (!skill.TotallyDisabled)
            {
                sb.Append(". " + skill.LevelDescriptor);
            }

            return sb.ToString().TrimEnd();
        }

        private static void BuildHealthItems(Pawn pawn, InspectionTreeItem healthNode)
        {
            healthNode.Children.Clear();
            var ptd = (PawnTreeData)healthNode.Data;

            if (pawn.health?.hediffSet == null)
                return;

            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0)
            {
                healthNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.Health, ptd.PawnIndex, healthNode));
                return;
            }

            foreach (var hediff in hediffs)
            {
                string label = hediff.LabelCap;
                if (hediff.Part != null)
                    label += " (" + hediff.Part.Label + ")";

                // Inline the full hediff info: mechanical effects + def description
                // (so screen-reader users don't need to open an info card)
                string tipExtra = hediff.TipStringExtra;
                string mechanical = !string.IsNullOrEmpty(tipExtra)
                    ? tipExtra.Replace("\n", ", ").TrimEnd(',', ' ')
                    : null;
                string description = hediff.def?.description;
                string tooltip;
                if (!string.IsNullOrEmpty(mechanical) && !string.IsNullOrEmpty(description))
                    tooltip = mechanical + ". " + description;
                else
                    tooltip = mechanical ?? description;

                healthNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Health, ptd.PawnIndex, healthNode,
                    tooltip: tooltip, domainData: hediff));
            }
        }

        private static void BuildPossessionItems(Pawn pawn, int pawnIndex, InspectionTreeItem possessionsNode)
        {
            possessionsNode.Children.Clear();

            var possessions = Find.GameInitData?.startingPossessions;
            if (possessions == null || !possessions.TryGetValue(pawn, out var items) || items.Count == 0)
            {
                possessionsNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.Possessions, pawnIndex, possessionsNode));
                return;
            }

            foreach (var item in items)
            {
                string label = item.Count > 1
                    ? $"{item.ThingDef.LabelCap} x{item.Count}"
                    : (string)item.ThingDef.LabelCap;

                string tooltip = item.ThingDef.LabelCap + "\n" + item.ThingDef.description;

                possessionsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Possessions, pawnIndex, possessionsNode,
                    tooltip: tooltip, domainData: item));
            }
        }

        public static List<FloatMenuOption> GetContextMenuOptions(int pawnIndex, Action rebuildCallback)
        {
            var options = new List<FloatMenuOption>();
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawnIndex < 0 || pawnIndex >= pawns.Count) return options;

            var pawn = pawns[pawnIndex];

            // Randomize (Alt+R)
            var randomizeOption = new FloatMenuOption("Randomize".Translate(), () =>
            {
                StartingPawnState.RandomizePawnAt(pawnIndex);
            });
            randomizeOption.tooltip = new TipSignal("Alt+R");
            options.Add(randomizeOption);

            // Rename (Alt+N)
            var renameOption = new FloatMenuOption("Rename".Translate(), () =>
            {
                StartingPawnState.RenamePawnAt(pawnIndex);
            });
            renameOption.tooltip = new TipSignal("Alt+N");
            options.Add(renameOption);

            // Edit pawn filter (Alt+F)
            var filterOption = new FloatMenuOption("Edit pawn filter...", () =>
            {
                PawnFilterState.Open();
            });
            filterOption.tooltip = new TipSignal("Alt+F");
            options.Add(filterOption);

            // Wanderer mode: Add/Remove pawn options
            if (StartingPawnState.Context == PawnEditorContext.Wanderer)
            {
                if (pawns.Count < 6)
                {
                    var addOption = new FloatMenuOption("Add pawn", () =>
                    {
                        WandererPatch.AddPawn();
                        rebuildCallback?.Invoke();
                    });
                    addOption.tooltip = new TipSignal("Alt+A");
                    options.Add(addOption);
                }
                if (pawns.Count > 1)
                {
                    var removeOption = new FloatMenuOption("Remove pawn", () =>
                    {
                        WandererPatch.RemovePawn(pawnIndex);
                        rebuildCallback?.Invoke();
                    });
                    removeOption.tooltip = new TipSignal("Delete");
                    options.Add(removeOption);
                }
            }

            // Biotech: Developmental stage and xenotype selectors
            if (ModsConfig.BiotechActive)
            {
                string devStageLabel = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
                var devStageOption = new FloatMenuOption(devStageLabel + "...", () =>
                {
                    var stageOptions = BuildDevStageOptions(pawnIndex, rebuildCallback);
                    WindowlessFloatMenuState.Open(stageOptions, colonistOrders: false);
                });
                devStageOption.tooltip = new TipSignal("DevelopmentalAgeSelectionDesc".Translate());
                options.Add(devStageOption);

                string xenoLabel = pawn.genes != null ? pawn.genes.XenotypeLabelCap : (string)"Xenotype".Translate();
                var xenoOption = new FloatMenuOption("Xenotype".Translate() + ": " + xenoLabel + "...", () =>
                {
                    var xenoOptions = BuildXenotypeOptions(pawnIndex, rebuildCallback, out var xenoDefs);
                    WindowlessFloatMenuState.Open(xenoOptions, colonistOrders: false, infoCardDefs: xenoDefs);
                });
                xenoOption.tooltip = new TipSignal("XenotypeSelectionDesc".Translate());
                options.Add(xenoOption);
            }

            return options;
        }

        private static List<FloatMenuOption> BuildDevStageOptions(int pawnIndex, Action rebuildCallback)
        {
            var options = new List<FloatMenuOption>();
            var request = StartingPawnUtility.GetGenerationRequest(pawnIndex);

            var stages = new[]
            {
                DevelopmentalStage.Adult,
                DevelopmentalStage.Child,
                DevelopmentalStage.Baby
            };

            string selectedSuffix = " (" + "StartingPawnsSelected".Translate().ToLower() + ")";

            foreach (var stage in stages)
            {
                var localStage = stage;
                bool isCurrent = request.AllowedDevelopmentalStages.Has(localStage);
                string label = localStage.ToString().Translate().CapitalizeFirst();

                if (isCurrent)
                {
                    label += selectedSuffix;
                    options.Add(new FloatMenuOption(label, () => { }));
                }
                else
                {
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        var req = StartingPawnUtility.GetGenerationRequest(pawnIndex);
                        req.AllowedDevelopmentalStages = localStage;
                        StartingPawnUtility.SetGenerationRequest(pawnIndex, req);
                        StartingPawnUtility.RandomizePawn(pawnIndex);
                        TolkHelper.Speak(localStage.ToString().Translate().CapitalizeFirst() + ", " + "Randomize".Translate());
                        rebuildCallback?.Invoke();
                    }));
                }
            }

            return options;
        }

        private static List<FloatMenuOption> BuildXenotypeOptions(int pawnIndex, Action rebuildCallback, out List<Def> infoCardDefs)
        {
            var options = new List<FloatMenuOption>();
            infoCardDefs = new List<Def>();

            // "Any (non-archite)" -- no info card
            options.Add(new FloatMenuOption("AnyNonArchite".Translate(), () =>
            {
                var req = StartingPawnUtility.GetGenerationRequest(pawnIndex);
                req.ForcedXenotype = null;
                req.ForcedCustomXenotype = null;
                req.AllowedXenotypes = null;
                req.ForceBaselinerChance = 0.5f;
                StartingPawnUtility.SetGenerationRequest(pawnIndex, req);
                StartingPawnUtility.RandomizePawn(pawnIndex);
                TolkHelper.Speak("AnyNonArchite".Translate() + ", " + "Randomize".Translate());
                rebuildCallback?.Invoke();
            }));
            infoCardDefs.Add(null);

            // Xenotype editor option (opens Dialog_CreateXenotype for full gene editing)
            options.Add(new FloatMenuOption("XenotypeEditor".Translate() + "...", () =>
            {
                Find.WindowStack.Add(new Dialog_CreateXenotype(pawnIndex, () =>
                {
                    CharacterCardUtility.cachedCustomXenotypes = null;
                    StartingPawnUtility.RandomizePawn(pawnIndex);
                    rebuildCallback?.Invoke();
                }));
            }));
            infoCardDefs.Add(null);

            // Standard xenotypes
            foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs.OrderBy(x => x.displayPriority))
            {
                var localXeno = xenotype;
                var xenoOption = new FloatMenuOption(localXeno.LabelCap, () =>
                {
                    var req = StartingPawnUtility.GetGenerationRequest(pawnIndex);
                    req.ForcedXenotype = localXeno;
                    req.ForcedCustomXenotype = null;
                    req.AllowedXenotypes = null;
                    req.ForceBaselinerChance = 0f;
                    StartingPawnUtility.SetGenerationRequest(pawnIndex, req);
                    StartingPawnUtility.RandomizePawn(pawnIndex);
                    TolkHelper.Speak(localXeno.LabelCap + ", " + "Randomize".Translate());
                    rebuildCallback?.Invoke();
                });
                string desc = localXeno.descriptionShort ?? localXeno.description;
                if (!string.IsNullOrEmpty(desc))
                    xenoOption.tooltip = new TipSignal(desc);
                options.Add(xenoOption);
                infoCardDefs.Add(localXeno);
            }

            return options;
        }

        /// <summary>
        /// Builds a summary string for a category node when collapsed.
        /// Returns null if no summary is appropriate (e.g. Skills).
        /// </summary>
        public static string GetCategorySummary(InspectionTreeItem categoryNode)
        {
            var ptd = categoryNode.Data as PawnTreeData;
            if (ptd == null || !ptd.CategoryType.HasValue || categoryNode.Children.Count == 0)
                return null;

            switch (ptd.CategoryType.Value)
            {
                case PawnCategoryType.Bio:
                    // Summarize key bio fields: first child is gender/age, then backstories
                    var bioParts = new List<string>();
                    for (int i = 0; i < categoryNode.Children.Count; i++)
                    {
                        string childLabel = categoryNode.Children[i].Label;
                        // Strip chronological age parenthetical from gender/age (first child)
                        // "Male, age 47 (117)" -> "Male, age 47"
                        if (i == 0)
                        {
                            int parenIdx = childLabel.LastIndexOf(" (");
                            if (parenIdx > 0 && childLabel.EndsWith(")"))
                                childLabel = childLabel.Substring(0, parenIdx);
                        }
                        bioParts.Add(childLabel);
                    }
                    return string.Join(", ", bioParts);

                case PawnCategoryType.Traits:
                    var traitNames = new List<string>();
                    foreach (var child in categoryNode.Children)
                        traitNames.Add(child.Label);
                    return string.Join(", ", traitNames);

                case PawnCategoryType.IncapableOf:
                    var tagNames = new List<string>();
                    foreach (var child in categoryNode.Children)
                        tagNames.Add(child.Label);
                    return string.Join(", ", tagNames);

                case PawnCategoryType.Health:
                    var healthNames = new List<string>();
                    foreach (var child in categoryNode.Children)
                        healthNames.Add(child.Label);
                    return string.Join(", ", healthNames);

                case PawnCategoryType.Skills:
                    return null; // Too many to summarize

                case PawnCategoryType.Possessions:
                    return null; // Items can be long, skip summary

                default:
                    return null;
            }
        }

        public static Pawn GetPawnAtIndex(int pawnIndex)
        {
            var pawns = Find.GameInitData?.startingAndOptionalPawns;
            if (pawns == null || pawnIndex < 0 || pawnIndex >= pawns.Count)
                return null;
            return pawns[pawnIndex];
        }

        public static int GetPawnCount()
        {
            return Find.GameInitData?.startingAndOptionalPawns?.Count ?? 0;
        }

        public static int GetStartingPawnCount()
        {
            return Find.GameInitData?.startingPawnCount ?? 0;
        }

        /// <summary>
        /// Gets the PawnTreeData from an InspectionTreeItem, or null if not present.
        /// </summary>
        public static PawnTreeData GetPawnData(InspectionTreeItem item)
        {
            return item?.Data as PawnTreeData;
        }
    }
}
