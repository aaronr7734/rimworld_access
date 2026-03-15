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

    public class PawnTreeItem
    {
        public string Label { get; set; }
        public string Tooltip { get; set; }
        public int IndentLevel { get; set; }
        public bool IsExpandable { get; set; }
        public bool IsExpanded { get; set; }
        public List<PawnTreeItem> Children { get; set; } = new List<PawnTreeItem>();
        public PawnTreeItem Parent { get; set; }
        public PawnNodeType NodeType { get; set; }
        public int PawnIndex { get; set; } = -1;
        public PawnCategoryType? CategoryType { get; set; }
        public object Data { get; set; }
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
        public static List<PawnTreeItem> BuildTree()
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int startingCount = Find.GameInitData.startingPawnCount;
            var hierarchy = new List<PawnTreeItem>();

            // Wanderer mode: all pawns are selected, no group headers
            if (StartingPawnState.Context == PawnEditorContext.Wanderer)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawnNode = BuildPawnNode(pawns[i], i, null);
                    hierarchy.Add(pawnNode);
                }
                return hierarchy;
            }

            // Game-start mode: pawns grouped under Selected/Left Behind headers
            var selectedHeader = new PawnTreeItem
            {
                Label = "StartingPawnsSelected".Translate(),
                IndentLevel = 0,
                NodeType = PawnNodeType.GroupHeader,
                IsExpandable = false
            };
            hierarchy.Add(selectedHeader);

            for (int i = 0; i < startingCount && i < pawns.Count; i++)
            {
                var pawnNode = BuildPawnNode(pawns[i], i, selectedHeader);
                selectedHeader.Children.Add(pawnNode);
                hierarchy.Add(pawnNode);
            }

            // Left behind group
            if (pawns.Count > startingCount)
            {
                var leftBehindHeader = new PawnTreeItem
                {
                    Label = "StartingPawnsLeftBehind".Translate(),
                    IndentLevel = 0,
                    NodeType = PawnNodeType.GroupHeader,
                    IsExpandable = false
                };
                hierarchy.Add(leftBehindHeader);

                for (int i = startingCount; i < pawns.Count; i++)
                {
                    var pawnNode = BuildPawnNode(pawns[i], i, leftBehindHeader);
                    leftBehindHeader.Children.Add(pawnNode);
                    hierarchy.Add(pawnNode);
                }
            }

            return hierarchy;
        }

        private static PawnTreeItem BuildPawnNode(Pawn pawn, int index, PawnTreeItem groupParent)
        {
            string nick = pawn.Name is NameTriple nameTriple
                ? (string.IsNullOrEmpty(nameTriple.Nick) ? nameTriple.First : nameTriple.Nick)
                : pawn.LabelShort;
            string title = pawn.story?.TitleCap;

            string label = string.IsNullOrEmpty(title) ? nick : $"{nick}, {title}";

            var pawnNode = new PawnTreeItem
            {
                Label = label,
                IndentLevel = 1,
                NodeType = PawnNodeType.Pawn,
                IsExpandable = true,
                IsExpanded = false,
                PawnIndex = index,
                Data = pawn,
                Parent = groupParent
            };

            BuildPawnCategories(pawn, index, pawnNode);
            return pawnNode;
        }

        private static void BuildPawnCategories(Pawn pawn, int pawnIndex, PawnTreeItem pawnNode)
        {
            pawnNode.Children.Clear();

            // Bio
            var bioNode = new PawnTreeItem
            {
                Label = "Bio",
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.Bio,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildBioItems(pawn, pawnIndex, bioNode);
            pawnNode.Children.Add(bioNode);

            // Traits
            var traitsNode = new PawnTreeItem
            {
                Label = "Traits".Translate(),
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.Traits,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildTraitItems(pawn, traitsNode);
            CollapseIfEmpty(traitsNode);
            pawnNode.Children.Add(traitsNode);

            // Incapable of
            var incapableNode = new PawnTreeItem
            {
                Label = "IncapableOf".Translate(pawn),
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.IncapableOf,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildIncapableOfItems(pawn, incapableNode);
            CollapseIfEmpty(incapableNode);
            pawnNode.Children.Add(incapableNode);

            // Skills
            var skillsNode = new PawnTreeItem
            {
                Label = "Skills".Translate(),
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.Skills,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildSkillItems(pawn, skillsNode);
            pawnNode.Children.Add(skillsNode);

            // Health
            var healthNode = new PawnTreeItem
            {
                Label = "Health".Translate(),
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.Health,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildHealthItems(pawn, healthNode);
            CollapseIfEmpty(healthNode);
            pawnNode.Children.Add(healthNode);

            // Possessions
            var possessionsNode = new PawnTreeItem
            {
                Label = "Possessions".Translate(),
                IndentLevel = 2,
                NodeType = PawnNodeType.Category,
                IsExpandable = true,
                CategoryType = PawnCategoryType.Possessions,
                PawnIndex = pawnIndex,
                Parent = pawnNode
            };
            BuildPossessionItems(pawn, pawnIndex, possessionsNode);
            CollapseIfEmpty(possessionsNode);
            pawnNode.Children.Add(possessionsNode);
        }

        /// <summary>
        /// If a category has only a single "None" child, make it non-expandable
        /// and fold "none" into the category label.
        /// </summary>
        private static void CollapseIfEmpty(PawnTreeItem categoryNode)
        {
            if (categoryNode.Children.Count == 1
                && categoryNode.Children[0].Label == "None".Translate())
            {
                categoryNode.IsExpandable = false;
                categoryNode.Label += ": " + "None".Translate();
                categoryNode.Children.Clear();
            }
        }

        private static void BuildBioItems(Pawn pawn, int pawnIndex, PawnTreeItem bioNode)
        {
            bioNode.Children.Clear();

            // Gender and age
            string genderAge = pawn.MainDesc(writeFaction: false);
            bioNode.Children.Add(new PawnTreeItem
            {
                Label = genderAge.CapitalizeFirst(),
                Tooltip = pawn.ageTracker?.AgeTooltipString,
                IndentLevel = 3,
                NodeType = PawnNodeType.Leaf,
                CategoryType = PawnCategoryType.Bio,
                PawnIndex = pawnIndex,
                Parent = bioNode
            });

            // Childhood backstory
            if (pawn.story != null)
            {
                var childhood = pawn.story.GetBackstory(BackstorySlot.Childhood);
                if (childhood != null)
                {
                    bioNode.Children.Add(new PawnTreeItem
                    {
                        Label = "Childhood".Translate() + ": " + childhood.TitleCapFor(pawn.gender),
                        Tooltip = childhood.FullDescriptionFor(pawn).Resolve(),
                        IndentLevel = 3,
                        NodeType = PawnNodeType.Leaf,
                        CategoryType = PawnCategoryType.Bio,
                        PawnIndex = pawnIndex,
                        Parent = bioNode
                    });
                }

                // Adulthood backstory
                var adulthood = pawn.story.GetBackstory(BackstorySlot.Adulthood);
                if (adulthood != null)
                {
                    bioNode.Children.Add(new PawnTreeItem
                    {
                        Label = "Adulthood".Translate() + ": " + adulthood.TitleCapFor(pawn.gender),
                        Tooltip = adulthood.FullDescriptionFor(pawn).Resolve(),
                        IndentLevel = 3,
                        NodeType = PawnNodeType.Leaf,
                        CategoryType = PawnCategoryType.Bio,
                        PawnIndex = pawnIndex,
                        Parent = bioNode
                    });
                }

                // Custom title
                if (pawn.story.title != null)
                {
                    bioNode.Children.Add(new PawnTreeItem
                    {
                        Label = "BackstoryTitle".Translate() + ": " + pawn.story.title,
                        IndentLevel = 3,
                        NodeType = PawnNodeType.Leaf,
                        CategoryType = PawnCategoryType.Bio,
                        PawnIndex = pawnIndex,
                        Parent = bioNode
                    });
                }
            }

            // Xenotype (Biotech)
            if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.GenesListForReading.Any())
            {
                string xenotypeLabel = pawn.genes.XenotypeLabelCap;
                string xenotypeDesc = pawn.genes.XenotypeDescShort;
                bioNode.Children.Add(new PawnTreeItem
                {
                    Label = "Xenotype".Translate() + ": " + xenotypeLabel,
                    Tooltip = xenotypeDesc,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Bio,
                    PawnIndex = pawnIndex,
                    Parent = bioNode,
                    Data = pawn.genes?.Xenotype
                });
            }

            // Faction
            if (pawn.Faction != null && !pawn.Faction.Hidden)
            {
                bioNode.Children.Add(new PawnTreeItem
                {
                    Label = "Faction".Translate() + ": " + pawn.Faction.Name,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Bio,
                    PawnIndex = pawnIndex,
                    Parent = bioNode
                });
            }

            // Ideology
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && pawn.Ideo != null)
            {
                bioNode.Children.Add(new PawnTreeItem
                {
                    Label = pawn.Ideo.name,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Bio,
                    PawnIndex = pawnIndex,
                    Parent = bioNode
                });

                // Role
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    bioNode.Children.Add(new PawnTreeItem
                    {
                        Label = role.LabelForPawn(pawn),
                        Tooltip = role.GetTip(),
                        IndentLevel = 3,
                        NodeType = PawnNodeType.Leaf,
                        CategoryType = PawnCategoryType.Bio,
                        PawnIndex = pawnIndex,
                        Parent = bioNode
                    });
                }
            }

            // Favorite color
            if (pawn.story?.favoriteColor != null)
            {
                string colorName = pawn.story.favoriteColor.LabelCap;
                bioNode.Children.Add(new PawnTreeItem
                {
                    Label = "FavoriteColorTooltip".Translate() + ": " + colorName,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Bio,
                    PawnIndex = pawnIndex,
                    Parent = bioNode
                });
            }
        }

        private static void BuildTraitItems(Pawn pawn, PawnTreeItem traitsNode)
        {
            traitsNode.Children.Clear();

            if (pawn.story?.traits == null || pawn.story.traits.allTraits.Count == 0)
            {
                string noTraitsLabel = pawn.DevelopmentalStage.Baby()
                    ? "TraitsDevelopLaterBaby".Translate()
                    : "None".Translate();
                traitsNode.Children.Add(new PawnTreeItem
                {
                    Label = noTraitsLabel,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Traits,
                    PawnIndex = traitsNode.PawnIndex,
                    Parent = traitsNode
                });
                return;
            }

            foreach (var trait in pawn.story.traits.TraitsSorted)
            {
                string label = trait.LabelCap;
                if (trait.Suppressed)
                    label += " (" + "Suppressed".Translate() + ")";

                traitsNode.Children.Add(new PawnTreeItem
                {
                    Label = label,
                    Tooltip = trait.TipString(pawn),
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Traits,
                    PawnIndex = traitsNode.PawnIndex,
                    Data = trait,
                    Parent = traitsNode
                });
            }
        }

        private static void BuildIncapableOfItems(Pawn pawn, PawnTreeItem incapableNode)
        {
            incapableNode.Children.Clear();

            WorkTags disabledTags = pawn.CombinedDisabledWorkTags;
            if (disabledTags == WorkTags.None)
            {
                incapableNode.Children.Add(new PawnTreeItem
                {
                    Label = "None".Translate(),
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.IncapableOf,
                    PawnIndex = incapableNode.PawnIndex,
                    Parent = incapableNode
                });
                return;
            }

            var tagsList = GetWorkTagsList(disabledTags);

            foreach (var tag in tagsList)
            {
                string tagLabel = tag.LabelTranslated().CapitalizeFirst();
                string tooltip = GetWorkTypeDisabledCausedBy(pawn, tag);

                incapableNode.Children.Add(new PawnTreeItem
                {
                    Label = tagLabel,
                    Tooltip = tooltip,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.IncapableOf,
                    PawnIndex = incapableNode.PawnIndex,
                    Data = tag,
                    Parent = incapableNode
                });
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

        private static void BuildSkillItems(Pawn pawn, PawnTreeItem skillsNode)
        {
            skillsNode.Children.Clear();

            if (pawn.DevelopmentalStage.Baby())
            {
                skillsNode.Children.Add(new PawnTreeItem
                {
                    Label = "SkillsDevelopLaterBaby".Translate(),
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Skills,
                    PawnIndex = skillsNode.PawnIndex,
                    Parent = skillsNode
                });
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

                skillsNode.Children.Add(new PawnTreeItem
                {
                    Label = label,
                    Tooltip = tooltip,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Skills,
                    PawnIndex = skillsNode.PawnIndex,
                    Data = skill,
                    Parent = skillsNode
                });
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

        private static void BuildHealthItems(Pawn pawn, PawnTreeItem healthNode)
        {
            healthNode.Children.Clear();

            if (pawn.health?.hediffSet == null)
                return;

            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0)
            {
                healthNode.Children.Add(new PawnTreeItem
                {
                    Label = "None".Translate(),
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Health,
                    PawnIndex = healthNode.PawnIndex,
                    Parent = healthNode
                });
                return;
            }

            foreach (var hediff in hediffs)
            {
                string label = hediff.LabelCap;
                if (hediff.Part != null)
                    label += " (" + hediff.Part.Label + ")";

                // Tooltip contains only the mechanical effects (label + body part already in Label)
                string tipExtra = hediff.TipStringExtra;
                string tooltip = !string.IsNullOrEmpty(tipExtra)
                    ? tipExtra.Replace("\n", ", ").TrimEnd(',', ' ')
                    : null;

                healthNode.Children.Add(new PawnTreeItem
                {
                    Label = label,
                    Tooltip = tooltip,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Health,
                    PawnIndex = healthNode.PawnIndex,
                    Data = hediff,
                    Parent = healthNode
                });
            }
        }

        private static void BuildPossessionItems(Pawn pawn, int pawnIndex, PawnTreeItem possessionsNode)
        {
            possessionsNode.Children.Clear();

            var possessions = Find.GameInitData?.startingPossessions;
            if (possessions == null || !possessions.TryGetValue(pawn, out var items) || items.Count == 0)
            {
                possessionsNode.Children.Add(new PawnTreeItem
                {
                    Label = "None".Translate(),
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Possessions,
                    PawnIndex = pawnIndex,
                    Parent = possessionsNode
                });
                return;
            }

            foreach (var item in items)
            {
                string label = item.Count > 1
                    ? $"{item.ThingDef.LabelCap} x{item.Count}"
                    : (string)item.ThingDef.LabelCap;

                string tooltip = item.ThingDef.LabelCap + "\n" + item.ThingDef.description;

                possessionsNode.Children.Add(new PawnTreeItem
                {
                    Label = label,
                    Tooltip = tooltip,
                    IndentLevel = 3,
                    NodeType = PawnNodeType.Leaf,
                    CategoryType = PawnCategoryType.Possessions,
                    PawnIndex = pawnIndex,
                    Data = item,
                    Parent = possessionsNode
                });
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
                StartingPawnUtility.RandomizePawn(pawnIndex);
                TolkHelper.Speak("Pawn randomized");
                rebuildCallback?.Invoke();
            });
            randomizeOption.tooltip = new TipSignal("Alt+R");
            options.Add(randomizeOption);

            // Rename (Alt+N)
            var renameOption = new FloatMenuOption("Rename".Translate(), () =>
            {
                var allFields = NameFilter.First | NameFilter.Nick | NameFilter.Last | NameFilter.Title;
                Find.WindowStack.Add(new Dialog_NamePawn(pawn, allFields, allFields, null));
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

            // "Any (non-archite)" — no info card
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
        public static string GetCategorySummary(PawnTreeItem categoryNode)
        {
            if (!categoryNode.CategoryType.HasValue || categoryNode.Children.Count == 0)
                return null;

            switch (categoryNode.CategoryType.Value)
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
    }
}
