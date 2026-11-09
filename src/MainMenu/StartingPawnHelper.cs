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
        Relations,
        Traits,
        IncapableOf,
        Skills,
        Health,
        Possessions,
        Abilities
    }

    /// <summary>How a Leaf row behaves when the cursor rests on it.</summary>
    public enum PawnLeafKind
    {
        /// <summary>Read-only text; Enter just re-announces.</summary>
        PlainText,
        /// <summary>First/Nick/Last name field; Enter opens a TextFieldEditSession.</summary>
        NameField,
        /// <summary>Developmental-stage selector; Enter opens the vanilla FloatMenu picker (Adult/Child/Baby).</summary>
        DevStageCombo,
        /// <summary>Xenotype selector; Enter opens the vanilla FloatMenu picker.</summary>
        XenotypeCombo,
        /// <summary>A top-stack "chip" or relation row whose Enter opens a vanilla dialog/tab (a click-vehicle, not a value change).</summary>
        InfoAction,
    }

    public enum NameFieldKind { First, Nick, Last }

    /// <summary>Pawn-specific tree metadata stored in InspectionTreeItem.Data.</summary>
    public class PawnTreeData
    {
        public PawnNodeType NodeType { get; set; }
        public int PawnIndex { get; set; } = -1;
        public PawnCategoryType? CategoryType { get; set; }
        /// <summary>Domain data: Pawn, Trait, SkillRecord, Hediff, ThingDefCount, XenotypeDef, WorkTags.</summary>
        public object DomainData { get; set; }

        /// <summary>Leaf shape; PlainText by default.</summary>
        public PawnLeafKind LeafKind { get; set; } = PawnLeafKind.PlainText;

        /// <summary>Which name part this leaf edits; set only for LeafKind.NameField.</summary>
        public NameFieldKind? NameField { get; set; }

        /// <summary>Enter's action for an InfoAction leaf, or null when Enter should just re-announce.</summary>
        public Action Activate { get; set; }

        /// <summary>Enter's handler for a Combo leaf: opens the vanilla FloatMenu picker.</summary>
        public Action OpenComboPicker { get; set; }

        /// <summary>The current value for a NameField/Combo leaf, spoken as the row's Value; Label is only the caption for those two kinds.</summary>
        public string ValueText { get; set; }

        /// <summary>
        /// True for the placeholder leaf a category builder adds when its collection is empty.
        /// CollapseIfEmpty reads this rather than comparing the label against "None".Translate(),
        /// since a category can hold one real item whose label reads "None" in some language.
        /// </summary>
        public bool IsEmptyPlaceholder { get; set; }

        /// <summary>Identity of a mod-contributed category (<see cref="StartingPawnHelper.RegisterCategoryExtender"/>), which has no <see cref="CategoryType"/>.</summary>
        public string ExtensionKey { get; set; }

        /// <summary>
        /// Pawn and category rows are identified by (node type, pawn index, category) rather than by
        /// reference, so a state-preserving rebuild still recognizes them. Their labels cannot carry
        /// that identity: a pawn's is its live summary and a category's can carry the pawn's name or
        /// a folded-in "None", so a randomize or rename would orphan the cursor and every expanded
        /// branch under it. Group headers and leaves deliberately keep reference equality — they
        /// share one tuple each, so value equality would let the preserver land on an arbitrary
        /// sibling instead of falling back to their own stable labels.
        /// </summary>
        public override bool Equals(object obj)
        {
            var other = obj as PawnTreeData;
            if (other == null || !HasStableIdentity || !other.HasStableIdentity)
            {
                return ReferenceEquals(this, obj);
            }
            return NodeType == other.NodeType
                && PawnIndex == other.PawnIndex
                && CategoryType == other.CategoryType
                && ExtensionKey == other.ExtensionKey;
        }

        public override int GetHashCode()
        {
            if (!HasStableIdentity)
            {
                return base.GetHashCode();
            }
            unchecked
            {
                int hash = (int)NodeType;
                hash = (hash * 397) + PawnIndex;
                hash = (hash * 397) + (CategoryType.HasValue ? (int)CategoryType.Value + 1 : 0);
                return (hash * 397) + (ExtensionKey != null ? ExtensionKey.GetHashCode() : 0);
            }
        }

        private bool HasStableIdentity
        {
            get { return NodeType == PawnNodeType.Pawn || NodeType == PawnNodeType.Category; }
        }
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
        /// <summary>Builds the pawn tree as InspectionTreeItem nodes under a hidden root.</summary>
        /// <param name="jumpToRelatedPawn">Wired into every Relations leaf's Activate; the owning ScreenScope supplies the cursor repositioning.</param>
        /// <param name="onMutated">
        /// Wired into the combo leaves' picker callbacks. REFRESH-ONLY by contract: each picker
        /// option already speaks its own confirmation, so announcing here would double-speak.
        /// </param>
        /// </param>
        public static InspectionTreeItem BuildTree(Action<Pawn> jumpToRelatedPawn, Action onMutated)
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
                    var pawnNode = BuildPawnNode(pawns[i], i, root, jumpToRelatedPawn, onMutated);
                    root.Children.Add(pawnNode);
                }
                return root;
            }

            // Selected / Left behind mirror the two header rows
            // Page_ConfigureStartingPawns.DrawPawnList draws: Selected always, Left behind exactly
            // when the loop reaches startingPawnCount. The boundary is a display grouping over one
            // flat roster, not a constraint — vanilla's reorder delegate moves pawns across it.
            var selectedGroup = MakeGroupNode("StartingPawnsSelected".Translate(), root);
            root.Children.Add(selectedGroup);
            for (int i = 0; i < startingCount && i < pawns.Count; i++)
            {
                selectedGroup.Children.Add(BuildPawnNode(pawns[i], i, selectedGroup, jumpToRelatedPawn, onMutated));
            }

            if (startingCount < pawns.Count)
            {
                var leftBehindGroup = MakeGroupNode("StartingPawnsLeftBehind".Translate(), root);
                root.Children.Add(leftBehindGroup);
                for (int i = startingCount; i < pawns.Count; i++)
                {
                    leftBehindGroup.Children.Add(BuildPawnNode(pawns[i], i, leftBehindGroup, jumpToRelatedPawn, onMutated));
                }
            }

            return root;
        }

        /// <summary>
        /// A top-level Selected / Left behind group, open by default. Group identity across a
        /// state-preserving rebuild rides the LABEL path segment rather than Data, which is safe
        /// because both labels are fixed vanilla strings.
        /// </summary>
        private static InspectionTreeItem MakeGroupNode(string label, InspectionTreeItem parent)
        {
            return new InspectionTreeItem
            {
                Label = label,
                IndentLevel = 0,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                IsExpanded = true,
                Parent = parent,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.GroupHeader,
                    PawnIndex = -1
                }
            };
        }

        private static InspectionTreeItem BuildPawnNode(Pawn pawn, int index, InspectionTreeItem parent,
            Action<Pawn> jumpToRelatedPawn, Action onMutated)
        {
            string nick = pawn.Name is NameTriple nameTriple
                ? (string.IsNullOrEmpty(nameTriple.Nick) ? nameTriple.First : nameTriple.Nick)
                : pawn.LabelShort;
            string title = pawn.story?.TitleCap;

            string shortLabel = string.IsNullOrEmpty(title) ? nick : $"{nick}, {title}";
            string verboseLabel = BuildCollapsedPawnLabel(pawn, shortLabel);

            var pawnNode = new InspectionTreeItem
            {
                Label = verboseLabel,
                ExpandedLabel = shortLabel,
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

            BuildPawnCategories(pawn, index, pawnNode, jumpToRelatedPawn, onMutated);
            return pawnNode;
        }

        private static void BuildPawnCategories(Pawn pawn, int pawnIndex, InspectionTreeItem pawnNode,
            Action<Pawn> jumpToRelatedPawn, Action onMutated)
        {
            pawnNode.Children.Clear();

            // Bio
            var bioNode = new InspectionTreeItem
            {
                Label = "TabCharacter".Translate().ToString(),
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
            BuildBioItems(pawn, pawnIndex, bioNode, onMutated);
            pawnNode.Children.Add(bioNode);

            // Relations (parity with the vanilla character card's Relations panel)
            var relationsNode = new InspectionTreeItem
            {
                Label = "Relations".Translate(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Relations,
                    PawnIndex = pawnIndex
                }
            };
            BuildRelationsItems(pawn, pawnIndex, relationsNode, jumpToRelatedPawn);
            CollapseIfEmpty(relationsNode);
            pawnNode.Children.Add(relationsNode);

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

            // Abilities (showOnCharacterCard abilities, CharacterCardUtility.cs:1249-1284)
            var abilitiesNode = new InspectionTreeItem
            {
                Label = "Abilities".Translate(pawn).ToString(),
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    CategoryType = PawnCategoryType.Abilities,
                    PawnIndex = pawnIndex
                }
            };
            BuildAbilityItems(pawn, abilitiesNode);
            CollapseIfEmpty(abilitiesNode);
            pawnNode.Children.Add(abilitiesNode);

            for (int i = 0; i < categoryExtenders.Count; i++)
            {
                try
                {
                    categoryExtenders[i](pawn, pawnIndex, pawnNode);
                }
                catch (Exception ex)
                {
                    ModLogger.Error("Starting pawn category extender failed: " + ex.Message);
                }
            }
        }

        /// <summary>A mod-compat builder appending its own category under each starting pawn, after the vanilla ones.</summary>
        public delegate void CategoryExtender(Pawn pawn, int pawnIndex, InspectionTreeItem pawnNode);

        private static readonly List<CategoryExtender> categoryExtenders = new List<CategoryExtender>();

        public static void RegisterCategoryExtender(CategoryExtender extender)
        {
            if (extender != null && !categoryExtenders.Contains(extender))
                categoryExtenders.Add(extender);
        }

        /// <summary>An extender's category node, identified by <paramref name="key"/> so a state-preserving rebuild recognizes it.</summary>
        public static InspectionTreeItem AddExtensionCategory(InspectionTreeItem pawnNode, string key, string label, int pawnIndex)
        {
            var node = new InspectionTreeItem
            {
                Label = label,
                IndentLevel = 2,
                Type = InspectionTreeItem.ItemType.Category,
                IsExpandable = true,
                Parent = pawnNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Category,
                    PawnIndex = pawnIndex,
                    ExtensionKey = key,
                }
            };
            pawnNode.Children.Add(node);
            return node;
        }

        /// <summary>Read-only text, or a button when <paramref name="activate"/> is given; a label provider keeps it live.</summary>
        public static InspectionTreeItem AddExtensionLeaf(InspectionTreeItem categoryNode, string label, int pawnIndex,
            Action activate = null, Func<string> labelProvider = null, string tooltip = null)
        {
            var ptd = categoryNode.Data as PawnTreeData;
            var leaf = new InspectionTreeItem
            {
                Label = label,
                LabelProvider = labelProvider,
                Tooltip = tooltip,
                IndentLevel = categoryNode.IndentLevel + 1,
                Type = InspectionTreeItem.ItemType.Item,
                Parent = categoryNode,
                Data = new PawnTreeData
                {
                    NodeType = PawnNodeType.Leaf,
                    PawnIndex = pawnIndex,
                    ExtensionKey = ptd != null ? ptd.ExtensionKey : null,
                    LeafKind = activate != null ? PawnLeafKind.InfoAction : PawnLeafKind.PlainText,
                    Activate = activate,
                }
            };
            categoryNode.Children.Add(leaf);
            return leaf;
        }

        /// <summary>Makes a category with only a "None" child non-expandable, folding "none" into its label.</summary>
        private static void CollapseIfEmpty(InspectionTreeItem categoryNode)
        {
            if (categoryNode.Children.Count == 1
                && (categoryNode.Children[0].Data as PawnTreeData)?.IsEmptyPlaceholder == true)
            {
                categoryNode.IsExpandable = false;
                categoryNode.Label += ": " + categoryNode.Children[0].Label;
                categoryNode.Children.Clear();
            }
        }

        private static InspectionTreeItem MakeLeaf(string label, int indentLevel, PawnCategoryType category, int pawnIndex, InspectionTreeItem parent,
            string tooltip = null, object domainData = null,
            PawnLeafKind leafKind = PawnLeafKind.PlainText, Action activate = null,
            NameFieldKind? nameField = null, Action openComboPicker = null,
            string valueText = null, bool isEmptyPlaceholder = false)
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
                    DomainData = domainData,
                    LeafKind = leafKind,
                    Activate = activate,
                    NameField = nameField,
                    OpenComboPicker = openComboPicker,
                    ValueText = valueText,
                    IsEmptyPlaceholder = isEmptyPlaceholder,
                }
            };
        }

        private static void BuildBioItems(Pawn pawn, int pawnIndex, InspectionTreeItem bioNode, Action onMutated)
        {
            bioNode.Children.Clear();

            // Specs harvested from CharacterCardUtility: First 12, Nick 16, Last 12, ValidNameRegex
            // "^[\p{L}0-9 '\-.]*$". Only meaningful for a NameTriple pawn; other names are covered
            // by the read-only main-desc leaf below.
            if (pawn.Name is NameTriple currentName)
            {
                bioNode.Children.Add(MakeLeaf(
                    "RimWorldAccess.StartingPawn.FirstName".Translate(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"FirstNameDesc".Translate(),
                    leafKind: PawnLeafKind.NameField, nameField: NameFieldKind.First,
                    valueText: currentName.First));
                bioNode.Children.Add(MakeLeaf(
                    "RimWorldAccess.StartingPawn.NickName".Translate(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"ShortIdentifierDesc".Translate(),
                    leafKind: PawnLeafKind.NameField, nameField: NameFieldKind.Nick,
                    valueText: currentName.Nick));
                bioNode.Children.Add(MakeLeaf(
                    "RimWorldAccess.StartingPawn.LastName".Translate(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"LastNameDesc".Translate(),
                    leafKind: PawnLeafKind.NameField, nameField: NameFieldKind.Last,
                    valueText: currentName.Last));
            }

            // Gender is folded in textually by MainDesc, so the Biotech gender-icon chip carries
            // nothing extra and gets no leaf of its own.
            string genderAge = pawn.MainDesc(writeFaction: false);
            bioNode.Children.Add(MakeLeaf(
                genderAge.CapitalizeFirst(), 3, PawnCategoryType.Bio, pawnIndex, bioNode,
                tooltip: pawn.ageTracker?.AgeTooltipString));

            // Enter opens the same vanilla FloatMenu picker the context menu builds; Left/Right
            // change nothing, since a combo box is a dropdown, not a slider.
            if (ModsConfig.BiotechActive)
            {
                bioNode.Children.Add(MakeLeaf(
                    "RimWorldAccess.StartingPawn.DevStageSelector".Translate(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"DevelopmentalAgeSelectionDesc".Translate(),
                    leafKind: PawnLeafKind.DevStageCombo,
                    openComboPicker: () => PawnContextMenuBuilder.OpenDevStagePicker(pawnIndex, onMutated),
                    valueText: pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst()));

                bioNode.Children.Add(MakeLeaf(
                    "RimWorldAccess.StartingPawn.XenotypeSelector".Translate(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"XenotypeSelectionDesc".Translate(),
                    leafKind: PawnLeafKind.XenotypeCombo,
                    openComboPicker: () => PawnContextMenuBuilder.OpenXenotypePicker(pawnIndex, onMutated),
                    valueText: pawn.genes != null ? pawn.genes.XenotypeLabelCap : (string)"Xenotype".Translate()));
            }

            // The top-stack "Xenotype" chip: click opens Dialog_ViewGenes in creation mode.
            if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.GenesListForReading.Any())
            {
                string xenotypeLabel = pawn.genes.XenotypeLabelCap;
                string xenotypeDesc = pawn.genes.XenotypeDescShort;
                bioNode.Children.Add(MakeLeaf(
                    "Xenotype".Translate() + ": " + xenotypeLabel,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: xenotypeDesc, domainData: pawn.genes?.Xenotype,
                    leafKind: PawnLeafKind.InfoAction,
                    activate: () => Find.WindowStack.Add(new Dialog_ViewGenes(pawn))));
            }

            // Faction chip: click opens Dialog_FactionDuringLanding in creation mode.
            if (pawn.Faction != null && !pawn.Faction.Hidden)
            {
                bioNode.Children.Add(MakeLeaf(
                    "Faction".Translate() + ": " + pawn.Faction.Name,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    leafKind: PawnLeafKind.InfoAction,
                    activate: () => Find.WindowStack.Add(new Dialog_FactionDuringLanding())));
            }

            // Extra-faction chips: quest-part/guest-status sourced, effectively never populated
            // pre-game, kept for parity and mod compat.
            var extraFactions = new List<ExtraFaction>();
            QuestUtility.GetExtraFactionsFromQuestParts(pawn, extraFactions);
            GuestUtility.GetExtraFactionsFromGuestStatus(pawn, extraFactions);
            foreach (var extra in extraFactions)
            {
                if (pawn.Faction == extra.faction) continue;
                string label = "RimWorldAccess.StartingPawn.ExtraFactionLabel".Translate(
                    extra.factionType.GetLabel().CapitalizeFirst(), extra.faction.Name);
                // Vanilla's click opens the Factions main tab, which does not exist during
                // creation: it gave the MAIN faction chip a creation-mode branch but not these, so
                // the click is a silent no-op for sighted players too. The chip is a real button,
                // so speak the reason rather than staying silent.
                bioNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    leafKind: PawnLeafKind.InfoAction,
                    activate: () => TolkHelper.Speak("RimWorldAccess.StartingPawn.ChipFactionsTabLater".Loc())));
            }

            // Ideoligion plate (Ideology, non-classic, has ideo): informational, no click in vanilla.
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && pawn.Ideo != null)
            {
                bioNode.Children.Add(MakeLeaf(
                    pawn.Ideo.name,
                    3, PawnCategoryType.Bio, pawnIndex, bioNode));

                // Vanilla's click opens ITab_Pawn_Social, which does not exist during creation, so
                // it is a silent no-op for sighted players too. A real button regardless, so speak
                // the reason rather than staying silent.
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    bioNode.Children.Add(MakeLeaf(
                        role.LabelForPawn(pawn),
                        3, PawnCategoryType.Bio, pawnIndex, bioNode,
                        tooltip: role.GetTip(),
                        leafKind: PawnLeafKind.InfoAction,
                        activate: () => TolkHelper.Speak("RimWorldAccess.StartingPawn.ChipSocialTabLater".Loc())));
                }
            }

            // Royal title chips, per title in effect: click opens Dialog_InfoCard.
            if (pawn.royalty != null && pawn.royalty.AllTitlesInEffectForReading.Count > 0)
            {
                foreach (var title in pawn.royalty.AllTitlesInEffectForReading)
                {
                    RoyalTitle localTitle = title;
                    int favor = pawn.royalty.GetFavor(localTitle.faction);
                    string titleLabel = localTitle.def.GetLabelCapFor(pawn) + " (" + favor + ")";
                    string tip = GetTitleTipString(pawn, localTitle.faction, localTitle, favor);
                    bioNode.Children.Add(MakeLeaf(
                        titleLabel, 3, PawnCategoryType.Bio, pawnIndex, bioNode,
                        tooltip: tip,
                        leafKind: PawnLeafKind.InfoAction,
                        activate: () => Find.WindowStack.Add(new Dialog_InfoCard(localTitle.def, localTitle.faction, pawn))));
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

            // Unrecruitable icon: effectively never populated pre-game, kept for parity.
            if (pawn.guest != null && !pawn.guest.Recruitable)
            {
                bioNode.Children.Add(MakeLeaf(
                    "Unrecruitable".Translate().AsTipTitle().CapitalizeFirst(),
                    3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    tooltip: (string)"UnrecruitableDesc".Translate(pawn.Named("PAWN"))));
            }

            // Quest line chips: click opens the Quests tab and selects the quest. Effectively never
            // populated pre-game, kept for parity.
            int questCount;
            var questLines = new List<(string Text, Quest Quest)>();
            QuestUtility.AppendInspectStringsFromQuestParts(
                (text, quest) => questLines.Add((text, quest)), pawn, out questCount);
            foreach (var (text, quest) in questLines)
            {
                Quest localQuest = quest;
                bioNode.Children.Add(MakeLeaf(
                    text, 3, PawnCategoryType.Bio, pawnIndex, bioNode,
                    leafKind: PawnLeafKind.InfoAction,
                    activate: () =>
                    {
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);
                        (MainButtonDefOf.Quests.TabWindow as MainTabWindow_Quests)?.Select(localQuest);
                    }));
            }

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
        }

        /// <summary>Reflects CharacterCardUtility's private GetTitleTipString — a pure read, no mutation-doctrine concern.</summary>
        private static string GetTitleTipString(Pawn pawn, Faction faction, RoyalTitle title, int favor)
        {
            try
            {
                var method = HarmonyLib.AccessTools.Method(typeof(CharacterCardUtility), "GetTitleTipString");
                return (string)method.Invoke(null, new object[] { pawn, faction, title, favor });
            }
            catch
            {
                return title.def.GetLabelCapFor(pawn);
            }
        }

        /// <summary>
        /// The per-pawn Relations items, mirroring the vanilla character card's Relations panel. A
        /// relation shows only when the other pawn is everSeenByPlayer and neither pawn hides
        /// relations, exactly SocialCardUtility.ShouldShowPawnRelations' rule; during creation every
        /// starting and optional pawn is flagged, so this resolves to the current roster.
        /// </summary>
        private static void BuildRelationsItems(Pawn pawn, int pawnIndex, InspectionTreeItem relationsNode, Action<Pawn> jumpToRelatedPawn)
        {
            relationsNode.Children.Clear();

            // Ordering mirrors SocialCardUtility's private CachedSocialTabEntryComparer, which
            // cannot be invoked directly: related first, then PawnRelationDef.importance
            // descending, then opinion of the other pawn descending.
            var relations = SocialTabHelper.GetRelations(pawn)
                .Where(r => r.OtherPawn?.relations != null
                            && r.OtherPawn.relations.everSeenByPlayer
                            && !r.OtherPawn.relations.hidePawnRelations
                            && !pawn.relations.hidePawnRelations)
                .Select(r => new { Relation = r, RelationDefs = pawn.GetRelations(r.OtherPawn).ToList() })
                .OrderByDescending(x => x.RelationDefs.Count > 0)
                .ThenByDescending(x => x.RelationDefs.Count > 0 ? x.RelationDefs.Max(d => d.importance) : float.MinValue)
                .ThenByDescending(x => x.Relation.MyOpinion)
                .Select(x => x.Relation)
                .ToList();

            if (relations.Count == 0)
            {
                relationsNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.Relations, pawnIndex, relationsNode,
                    isEmptyPlaceholder: true));
                return;
            }

            foreach (var r in relations)
            {
                string label = $"{r.OtherPawnName}, {string.Join(", ", r.Relations)}";
                // Opinions live in the tooltip so the collapsed summary stays concise, and are read
                // when the cursor lands on the relation. Reuses SocialTabHelper's own opinion keys.
                string tooltip = "RimWorldAccess.Pawns.Social.Relation.MyOpinion".Translate(r.MyOpinion.ToString("+0;-0;0"))
                    + ". " + "RimWorldAccess.Pawns.Social.Relation.TheirOpinion".Translate(r.TheirOpinion.ToString("+0;-0;0"));
                Pawn other = r.OtherPawn;
                relationsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Relations, pawnIndex, relationsNode,
                    tooltip: tooltip, domainData: other,
                    leafKind: PawnLeafKind.InfoAction,
                    activate: jumpToRelatedPawn == null ? (Action)null : () => jumpToRelatedPawn(other)));
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
                    noTraitsLabel, 3, PawnCategoryType.Traits, ptd.PawnIndex, traitsNode,
                    isEmptyPlaceholder: true));
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
                    "None".Translate(), 3, PawnCategoryType.IncapableOf, ptd.PawnIndex, incapableNode,
                    isEmptyPlaceholder: true));
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
        /// Reflects CharacterCardUtility's private GetWorkTypeDisableCauses — the cause list the
        /// character card's IncapableOf tooltip reads — and formats each cause as vanilla's own
        /// GetWorkTypeDisabledCausedBy does, reusing its Translate keys, so the DLC-only royal
        /// title, quest, ideoligion role and mutant causes a hand-rolled subset would drop are
        /// covered. Falls back to the Backstory/Trait/Hediff/Gene subset only if the private method
        /// cannot be found.
        /// </summary>
        private static string GetWorkTypeDisabledCausedBy(Pawn pawn, WorkTags workTag)
        {
            var sb = new StringBuilder();
            List<object> causes = null;
            try
            {
                var method = HarmonyLib.AccessTools.Method(typeof(CharacterCardUtility), "GetWorkTypeDisableCauses");
                causes = method?.Invoke(null, new object[] { pawn, workTag }) as List<object>;
            }
            catch
            {
                causes = null;
            }

            if (causes != null)
            {
                foreach (var item in causes)
                {
                    if (item is BackstoryDef backstoryDef)
                        sb.AppendLine("IncapableOfTooltipBackstory".Translate() + ": " + backstoryDef.TitleFor(pawn.gender).CapitalizeFirst());
                    else if (item is Trait trait)
                        sb.AppendLine("IncapableOfTooltipTrait".Translate() + ": " + trait.LabelCap);
                    else if (item is Hediff hediff)
                        sb.AppendLine("IncapableOfTooltipHediff".Translate() + ": " + hediff.LabelCap);
                    else if (item is RoyalTitle royalTitle)
                        sb.AppendLine("IncapableOfTooltipTitle".Translate() + ": " + royalTitle.def.GetLabelFor(pawn));
                    else if (item is Quest quest)
                        sb.AppendLine("IncapableOfTooltipQuest".Translate() + ": " + quest.name);
                    else if (item is Precept_Role preceptRole)
                        sb.AppendLine("IncapableOfTooltipRole".Translate() + ": " + preceptRole.LabelForPawn(pawn));
                    else if (item is Gene gene)
                        sb.AppendLine("IncapableOfTooltipGene".Translate() + ": " + gene.LabelCap);
                    else if (item is MutantDef mutantDef)
                        sb.AppendLine("IncapableOfTooltipMutant".Translate() + ": " + mutantDef.LabelCap);
                }
            }
            else
            {
                // Fallback subset if the private method is missing.
                if (pawn.story?.Childhood != null && (pawn.story.Childhood.workDisables & workTag) != WorkTags.None)
                    sb.AppendLine("IncapableOfTooltipBackstory".Translate() + ": " + pawn.story.Childhood.TitleFor(pawn.gender).CapitalizeFirst());
                if (pawn.story?.Adulthood != null && (pawn.story.Adulthood.workDisables & workTag) != WorkTags.None)
                    sb.AppendLine("IncapableOfTooltipBackstory".Translate() + ": " + pawn.story.Adulthood.TitleFor(pawn.gender).CapitalizeFirst());

                if (pawn.story?.traits != null)
                {
                    foreach (var trait in pawn.story.traits.allTraits)
                    {
                        if (!trait.Suppressed && (trait.def.disabledWorkTags & workTag) != WorkTags.None)
                            sb.AppendLine("IncapableOfTooltipTrait".Translate() + ": " + trait.LabelCap);
                    }
                }

                if (pawn.health?.hediffSet != null)
                {
                    foreach (var hediff in pawn.health.hediffSet.hediffs)
                    {
                        var stage = hediff.CurStage;
                        if (stage != null && (stage.disabledWorkTags & workTag) != WorkTags.None)
                            sb.AppendLine("IncapableOfTooltipHediff".Translate() + ": " + hediff.LabelCap);
                    }
                }

                if (ModsConfig.BiotechActive && pawn.genes != null)
                {
                    foreach (var gene in pawn.genes.GenesListForReading)
                    {
                        if (gene.Active && (gene.def.disabledWorkTags & workTag) != WorkTags.None)
                            sb.AppendLine("IncapableOfTooltipGene".Translate() + ": " + gene.LabelCap);
                    }
                }
            }

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

            // The game's own display order (listOrder descending).
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

                string tooltip = BuildSkillTooltip(skill);

                skillsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Skills, ptd.PawnIndex, skillsNode,
                    tooltip: tooltip, domainData: skill));
            }
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
                    "None".Translate(), 3, PawnCategoryType.Health, ptd.PawnIndex, healthNode,
                    isEmptyPlaceholder: true));
                return;
            }

            foreach (var hediff in hediffs)
            {
                string label = hediff.LabelCap;
                if (hediff.Part != null)
                    label += " (" + hediff.Part.Label + ")";

                // Inline the full hediff info so no info card is needed.
                string mechanical = null;
                string tipExtra = hediff.TipStringExtra;
                if (!string.IsNullOrWhiteSpace(tipExtra))
                {
                    // Collapse newlines to ", " so the text reads as one sentence rather than
                    // reaching the screen reader with stray line breaks.
                    mechanical = tipExtra
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Trim()
                        .Replace("\n", ", ");
                    if (string.IsNullOrWhiteSpace(mechanical)) mechanical = null;
                }

                string description = hediff.def?.description?.Trim();

                string tooltip;
                if (!string.IsNullOrEmpty(mechanical) && !string.IsNullOrEmpty(description))
                {
                    char last = mechanical[mechanical.Length - 1];
                    string sep = (last == '.' || last == '!' || last == '?') ? " " : ". ";
                    tooltip = mechanical + sep + description;
                }
                else
                    tooltip = mechanical ?? description;

                healthNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Health, ptd.PawnIndex, healthNode,
                    tooltip: tooltip, domainData: hediff));
            }

            // Bleeding-rate footer, mirroring HealthCardUtility.DrawHediffListing.
            float bleedRate = pawn.health.hediffSet.BleedRateTotal;
            if (bleedRate > 0.01f)
            {
                string footer = "BleedingRate".Translate() + ": " + bleedRate.ToStringPercent() + "/" + "LetterDay".Translate();
                int ticksUntilDeath = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
                if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.HasActiveGene(GeneDefOf.Deathless))
                {
                    footer += " (" + "Deathless".Translate() + ")";
                }
                else if (ticksUntilDeath >= 60000)
                {
                    footer += " (" + "WontBleedOutSoon".Translate() + ")";
                }
                else
                {
                    footer += " (" + "TimeToDeath".Translate(ticksUntilDeath.ToStringTicksToPeriod()) + ")";
                }
                healthNode.Children.Add(MakeLeaf(
                    footer, 3, PawnCategoryType.Health, ptd.PawnIndex, healthNode));
            }
        }

        private static void BuildPossessionItems(Pawn pawn, int pawnIndex, InspectionTreeItem possessionsNode)
        {
            possessionsNode.Children.Clear();

            var possessions = Find.GameInitData?.startingPossessions;
            if (possessions == null || !possessions.TryGetValue(pawn, out var items) || items.Count == 0)
            {
                possessionsNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.Possessions, pawnIndex, possessionsNode,
                    isEmptyPlaceholder: true));
                return;
            }

            foreach (var item in items)
            {
                // Vanilla's own row label, not a hand-built "name xN": DrawPossessions draws
                // ThingDefCount.LabelCap, which runs the count through GenLabel.ThingLabel and so
                // localizes and pluralizes it.
                string label = item.LabelCap;

                string tooltip = item.ThingDef.LabelCap + "\n" + item.ThingDef.description;

                possessionsNode.Children.Add(MakeLeaf(
                    label, 3, PawnCategoryType.Possessions, pawnIndex, possessionsNode,
                    tooltip: tooltip, domainData: item));
            }
        }

        /// <summary>Abilities category (CharacterCardUtility.cs:1249-1284).</summary>
        private static void BuildAbilityItems(Pawn pawn, InspectionTreeItem abilitiesNode)
        {
            abilitiesNode.Children.Clear();
            var ptd = (PawnTreeData)abilitiesNode.Data;

            var abilities = pawn.abilities?.abilities?
                .Where(a => a.def.showOnCharacterCard)
                .ToList();

            if (abilities == null || abilities.Count == 0)
            {
                abilitiesNode.Children.Add(MakeLeaf(
                    "None".Translate(), 3, PawnCategoryType.Abilities, ptd.PawnIndex, abilitiesNode,
                    isEmptyPlaceholder: true));
                return;
            }

            foreach (var ability in abilities)
            {
                string tooltip = ability.Tooltip;
                abilitiesNode.Children.Add(MakeLeaf(
                    ability.def.LabelCap, 3, PawnCategoryType.Abilities, ptd.PawnIndex, abilitiesNode,
                    tooltip: tooltip, domainData: ability.def));
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

        /// <summary>The PawnTreeData on an item, or null.</summary>
        public static PawnTreeData GetPawnData(InspectionTreeItem item)
        {
            return item?.Data as PawnTreeData;
        }

        // ------------------------------------------------------------------
        // Label formatting: pure string builders, consumed by the tree construction above and by
        // StartingPawnScreenScope's collapsed-row Extras fragment.
        // ------------------------------------------------------------------

        public static string GetPassionLabel(Passion passion)
        {
            switch (passion)
            {
                case Passion.Minor:
                    return "PassionMinor".Translate();
                case Passion.Major:
                    return "PassionMajor".Translate();
                case Passion.None:
                    return "";
                default:
                    return passion.GetLabel();
            }
        }

        /// <summary>
        /// The collapsed-state SUPPLEMENT for a pawn node: age, gender, traits, and the top five
        /// non-disabled skills with passions — everything BEYOND the short name/title label, which
        /// the row speaks separately and this never repeats.
        /// </summary>
        public static string BuildCollapsedPawnLabel(Pawn pawn, string shortLabel)
        {
            if (pawn == null) return "";

            var parts = new List<string>();

            parts.Add($"{"Stat_Age_Label".Translate()}: {pawn.ageTracker.AgeBiologicalYears}");

            string gender = pawn.gender.GetLabel().CapitalizeFirst();
            if (!string.IsNullOrEmpty(gender))
                parts.Add($"{"Gender".Translate()}: {gender}");

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

            if (pawn.skills?.skills != null)
            {
                var topSkills = pawn.skills.skills
                    .Where(s => !s.TotallyDisabled)
                    .OrderByDescending(s => s.Level)
                    .Take(5)
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

        public static string BuildSkillTooltip(SkillRecord skill)
        {
            return RimWorldAccess.Shell.SpeechFlatten.ToSentences(string.Join("\n", PawnSkillsAdapter.TooltipLines(skill)));
        }

        /// <summary>A collapsed category node's summary, or null where none is appropriate (Skills).</summary>
        public static string GetCategorySummary(InspectionTreeItem categoryNode)
        {
            var ptd = categoryNode.Data as PawnTreeData;
            if (ptd == null || !ptd.CategoryType.HasValue || categoryNode.Children.Count == 0)
                return null;

            switch (ptd.CategoryType.Value)
            {
                case PawnCategoryType.Bio:
                    return BuildBioSummary(categoryNode);

                // These all summarize identically: join every child's label with ", ".
                case PawnCategoryType.Relations:
                case PawnCategoryType.Traits:
                case PawnCategoryType.IncapableOf:
                case PawnCategoryType.Health:
                case PawnCategoryType.Abilities:
                    return JoinChildLabels(categoryNode);

                case PawnCategoryType.Skills:
                    return null; // Too many to summarize

                case PawnCategoryType.Possessions:
                    return null; // Items can be long, skip summary

                default:
                    return null;
            }
        }

        private static string BuildBioSummary(InspectionTreeItem categoryNode)
        {
            // Skip name-field and combo rows, reachable as their own leaves; join the plain
            // descriptive leaves only.
            var bioParts = new List<string>();
            foreach (var child in categoryNode.Children)
            {
                var ptd = child.Data as PawnTreeData;
                if (ptd != null && (ptd.LeafKind == PawnLeafKind.NameField
                    || ptd.LeafKind == PawnLeafKind.DevStageCombo
                    || ptd.LeafKind == PawnLeafKind.XenotypeCombo))
                {
                    continue;
                }
                bioParts.Add(child.Label);
            }
            return string.Join(", ", bioParts);
        }

        private static string JoinChildLabels(InspectionTreeItem categoryNode)
        {
            var names = new List<string>();
            foreach (var child in categoryNode.Children)
                names.Add(child.Label);
            return string.Join(", ", names);
        }
    }
}
