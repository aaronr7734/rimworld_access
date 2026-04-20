using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds InspectionTreeItem trees from Dialog_InfoCard data.
    /// Supports all tabs: Stats, Character, Health, Records, Permits.
    /// </summary>
    public static class InfoCardTreeBuilder
    {
        /// <summary>
        /// Builds the complete tree for an info card, with all available tabs.
        /// </summary>
        public static InspectionTreeItem BuildTree(Dialog_InfoCard dialog)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = GetRootLabel(dialog),
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            var availableTabs = InfoCardDataExtractor.GetAvailableTabs(dialog);

            // If only one tab, skip the tab level and build contents directly under root
            if (availableTabs.Count == 1)
            {
                BuildTabChildren(root, dialog, availableTabs[0]);
                return root;
            }

            // Multiple tabs - create tab nodes
            foreach (var tab in availableTabs)
            {
                var tabNode = CreateTabNode(dialog, tab);
                AddChild(root, tabNode);
            }

            // Add Actions tab if pawn has available actions (but not in modal contexts)
            var thing = InfoCardDataExtractor.GetThing(dialog);
            if (thing is Pawn pawn && CanPawnBeRenamed(pawn) && !IsInModalContext())
            {
                var actionsTab = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "Actions",
                    Data = pawn,
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = 0
                };

                actionsTab.OnActivate = () => BuildActionsTabChildren(actionsTab, pawn);
                AddChild(root, actionsTab);
            }

            return root;
        }

        /// <summary>
        /// Gets the root label for the info card based on what's being displayed.
        /// </summary>
        private static string GetRootLabel(Dialog_InfoCard dialog)
        {
            string infoCardLabel = ConceptDefOf.InfoCard.label.CapitalizeFirst();

            var thing = InfoCardDataExtractor.GetThing(dialog);
            if (thing != null)
            {
                return $"{infoCardLabel}: {thing.LabelCapNoCount}";
            }

            var worldObject = InfoCardDataExtractor.GetWorldObject(dialog);
            if (worldObject != null)
            {
                return $"{infoCardLabel}: {worldObject.LabelCap}";
            }

            var hediff = InfoCardDataExtractor.GetHediff(dialog);
            if (hediff != null)
            {
                return $"{infoCardLabel}: {hediff.def.LabelCap}";
            }

            var def = InfoCardDataExtractor.GetDef(dialog);
            var stuff = InfoCardDataExtractor.GetStuff(dialog);

            if (def is ThingDef thingDef && stuff != null)
            {
                return $"{infoCardLabel}: {GenLabel.ThingLabel(thingDef, stuff).CapitalizeFirst()}";
            }

            if (def is AbilityDef abilityDef)
            {
                return $"{infoCardLabel}: {abilityDef.LabelCap}";
            }

            var titleDef = InfoCardDataExtractor.GetTitleDef(dialog);
            if (titleDef != null)
            {
                return $"{infoCardLabel}: {titleDef.GetLabelCapForBothGenders()}";
            }

            var faction = InfoCardDataExtractor.GetFaction(dialog);
            if (faction != null)
            {
                return $"{infoCardLabel}: {faction.Name}";
            }

            if (def != null)
            {
                return $"{infoCardLabel}: {def.LabelCap}";
            }

            return infoCardLabel;
        }

        /// <summary>
        /// Creates a tab node with lazy-loaded children.
        /// </summary>
        private static InspectionTreeItem CreateTabNode(Dialog_InfoCard dialog, Dialog_InfoCard.InfoCardTab tab)
        {
            string tabLabel = GetTabLabel(tab);

            var tabNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = tabLabel,
                Data = tab,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = 0
            };

            // Lazy load children when expanded, and sync visual tab
            tabNode.OnActivate = () =>
            {
                dialog.SetTab(tab);
                BuildTabChildren(tabNode, dialog, tab);
            };

            return tabNode;
        }

        /// <summary>
        /// Gets the display label for a tab.
        /// </summary>
        private static string GetTabLabel(Dialog_InfoCard.InfoCardTab tab)
        {
            switch (tab)
            {
                case Dialog_InfoCard.InfoCardTab.Stats: return "TabStats".Translate();
                case Dialog_InfoCard.InfoCardTab.Character: return "TabCharacter".Translate();
                case Dialog_InfoCard.InfoCardTab.Health: return "TabHealth".Translate();
                case Dialog_InfoCard.InfoCardTab.Records: return "TabRecords".Translate();
                case Dialog_InfoCard.InfoCardTab.Permits: return "TabPermits".Translate();
                default: return tab.ToString();
            }
        }

        /// <summary>
        /// Builds children for a specific tab.
        /// </summary>
        private static void BuildTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog, Dialog_InfoCard.InfoCardTab tab)
        {
            if (tabNode.Children.Count > 0)
                return; // Already built

            switch (tab)
            {
                case Dialog_InfoCard.InfoCardTab.Stats:
                    BuildStatsTabChildren(tabNode, dialog);
                    break;
                case Dialog_InfoCard.InfoCardTab.Character:
                    BuildCharacterTabChildren(tabNode, dialog);
                    break;
                case Dialog_InfoCard.InfoCardTab.Health:
                    BuildHealthTabChildren(tabNode, dialog);
                    break;
                case Dialog_InfoCard.InfoCardTab.Records:
                    BuildRecordsTabChildren(tabNode, dialog);
                    break;
                case Dialog_InfoCard.InfoCardTab.Permits:
                    BuildPermitsTabChildren(tabNode, dialog);
                    break;
            }

            // Smart labels for lazy-loaded nodes are set inline at creation time
            // (ExpandedLabel = short form, Label = aggregated form) since
            // BuildSmartLabels can't work on nodes whose children haven't been
            // populated yet.
        }

        #region Stats Tab

        private static void BuildStatsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var entries = InfoCardDataExtractor.GetStatEntries();
            if (entries.Count == 0)
            {
                AddChild(tabNode, CreateInfoItem("No stats available", tabNode.IndentLevel + 1));
                return;
            }

            // Pre-fetch the dialog's thing for gene label enrichment (avoids repeated reflection)
            var dialogThing = InfoCardDataExtractor.GetThing(dialog);
            GeneSetHolderBase geneSetHolder = dialogThing as GeneSetHolderBase;
            string genesTranslated = ModsConfig.BiotechActive ? "Genes".Translate().CapitalizeFirst().ToString() : null;

            // Group by category label (not object) to avoid duplicate headers for same-named categories
            var grouped = entries
                .GroupBy(e => e.category.LabelCap.ToString())
                .Select(g => new { Label = g.Key, Entries = g.ToList(), DisplayOrder = g.First().category.displayOrder })
                .OrderBy(g => g.DisplayOrder);

            foreach (var group in grouped)
            {
                // Add stat items under this category (category name stored in Description for section announcements)
                var sortedEntries = group.Entries.OrderByDescending(e => e.DisplayPriorityWithinCategory);
                // Get the dialog's Def for xenotype-specific handling
                var dialogDef = InfoCardDataExtractor.GetDef(dialog);

                foreach (var entry in sortedEntries)
                {
                    string value = entry.ValueString;
                    bool emptyValue = string.IsNullOrEmpty(value);

                    // When value is empty (e.g. Description), use first non-redundant line of explanation text
                    if (emptyValue)
                    {
                        try
                        {
                            string explanation = entry.GetExplanationText(StatRequest.ForEmpty())?.Trim();
                            if (!string.IsNullOrEmpty(explanation))
                            {
                                // Skip lines that just repeat the entry label (e.g., "Required apparel:" header)
                                string normEntryLabel = entry.LabelCap.ToString().TrimEnd(':', '.', ' ').ToLowerInvariant();
                                var lines = explanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    string trimmed = line.Trim();
                                    string normLine = trimmed.TrimEnd(':', '.', ' ').ToLowerInvariant();
                                    if (!string.IsNullOrEmpty(trimmed) && normLine != normEntryLabel)
                                    {
                                        value = trimmed;
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    string entryLabel = entry.LabelCap.ToString();
                    // Normalize for comparison — RimWorld sometimes returns values
                    // that match the label but with trailing punctuation (e.g., "Required apparel:")
                    if (!emptyValue)
                    {
                        string normalizedValue = value?.TrimEnd(':', '.', ' ') ?? "";
                        string normalizedLabel = entryLabel.TrimEnd(':', '.', ' ');
                        if (normalizedValue.ToLowerInvariant() == normalizedLabel.ToLowerInvariant())
                        {
                            // Value is redundant with label — treat as empty and re-extract from explanation
                            emptyValue = true;
                            value = null;
                            try
                            {
                                string explanation = entry.GetExplanationText(StatRequest.ForEmpty())?.Trim();
                                if (!string.IsNullOrEmpty(explanation))
                                {
                                    // Find the first non-redundant line
                                    var lines = explanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var line in lines)
                                    {
                                        string trimmed = line.Trim();
                                        string normLine = trimmed.TrimEnd(':', '.', ' ').ToLowerInvariant();
                                        if (!string.IsNullOrEmpty(trimmed) && normLine != normalizedLabel.ToLowerInvariant())
                                        {
                                            value = trimmed;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    string label;
                    if (string.IsNullOrEmpty(value))
                        label = entryLabel;
                    else
                        label = $"{entryLabel}: {value}";

                    // Enrich gene labels for GeneSetHolderBase items with shade-aware descriptions.
                    // Also suppress the useless explanation (just a header like "Genes:") since
                    // the label already contains all gene names with shade descriptions.
                    bool suppressExplanation = false;
                    if (geneSetHolder?.GeneSet != null &&
                        genesTranslated != null &&
                        entry.LabelCap.ToString() == genesTranslated)
                    {
                        var genes = geneSetHolder.GeneSet.GenesListForReading;
                        if (genes != null && genes.Count > 0)
                        {
                            string shadeAwareValue = string.Join(", ", genes.Select(g => GeneTreeBuilder.GetGeneDisplayLabel(g)));
                            label = $"{entry.LabelCap}: {shadeAwareValue}";
                        }
                        suppressExplanation = true;
                    }

                    // Enrich label with hyperlink def names when the entry has a real value.
                    // Skip for empty-value entries (like Description) — those get explanation text
                    // as their label instead, and hyperlinks remain accessible via Alt+I.
                    if (!emptyValue)
                    {
                        try
                        {
                            var hyperlinks = entry.GetHyperlinks(StatRequest.ForEmpty());
                            if (hyperlinks != null)
                            {
                                var defNames = new List<string>();
                                foreach (var link in hyperlinks)
                                {
                                    string name = link.def?.label ?? link.thing?.def?.label;
                                    if (!string.IsNullOrEmpty(name) && !label.ToLower().Contains(name.ToLower()))
                                        defNames.Add(name.CapitalizeFirst());
                                }
                                if (defNames.Count > 0)
                                    label += $" ({string.Join(", ", defNames)})";
                            }
                        }
                        catch { }
                    }

                    // Check explanation text upfront to determine expandability
                    bool hasExplanation = false;
                    string explanationText = null;
                    if (!suppressExplanation)
                    {
                        try
                        {
                            explanationText = entry.GetExplanationText(StatRequest.ForEmpty())?.Trim();
                            hasExplanation = !string.IsNullOrEmpty(explanationText);
                        }
                        catch { }
                    }

                    // Skip entries with no value and no explanation (e.g., Description for things without one)
                    if (emptyValue && !hasExplanation)
                        continue;

                    // For expandable stat nodes, set ExpandedLabel (short form shown when expanded)
                    // and aggregate explanation into Label (full form shown when collapsed).
                    // For empty-value stats (like "Description"), the label already contains
                    // the explanation text as its value, so we do NOT re-aggregate it.
                    string statExpandedLabel = null;
                    if (hasExplanation && explanationText != null)
                    {
                        if (emptyValue)
                        {
                            // Empty-value stat: aggregate ALL non-redundant explanation lines
                            statExpandedLabel = entry.LabelCap.ToString();
                            string cleanExplanation = explanationText.StripTags();
                            var explanationLines = cleanExplanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            string normLabel = entryLabel.TrimEnd(':', '.', ' ').ToLowerInvariant();
                            string aggregated = string.Join(". ", explanationLines
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrEmpty(l) && l.TrimEnd(':', '.', ' ').ToLowerInvariant() != normLabel));
                            if (!string.IsNullOrEmpty(aggregated))
                                label = statExpandedLabel + ": " + aggregated;
                        }
                        else
                        {
                            // Stat with real value: label is "StatName: Value"
                            // ExpandedLabel = that label; Label gets explanation appended
                            statExpandedLabel = label;
                            string cleanExplanation = explanationText.StripTags();
                            var explanationLines = cleanExplanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            if (explanationLines.Length > 0)
                            {
                                // Filter out lines that are redundant with the entry label
                                string normLabel = entry.LabelCap.ToString().TrimEnd(':', '.', ' ').ToLowerInvariant();
                                string aggregated = string.Join(". ", explanationLines
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrEmpty(l) && l.TrimEnd(':', '.', ' ').ToLowerInvariant() != normLabel));
                                if (!string.IsNullOrEmpty(aggregated))
                                    label = statExpandedLabel + ". " + aggregated;
                            }
                        }
                    }

                    var statNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        ExpandedLabel = statExpandedLabel,
                        Description = group.Label,
                        Data = emptyValue ? null : (object)entry,
                        IsExpandable = hasExplanation,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasExplanation)
                    {
                        // For the "Genes" entry on a XenotypeDef, build individual inspectable gene nodes
                        if (dialogDef is XenotypeDef xenoDef && genesTranslated != null &&
                            entry.LabelCap.ToString() == genesTranslated)
                        {
                            statNode.Data = xenoDef.genes;
                            statNode.OnActivate = () => BuildXenotypeGeneChildren(statNode, xenoDef);
                        }
                        else
                        {
                            statNode.OnActivate = () => BuildStatDetailChildren(statNode, entry);
                        }
                    }

                    AddChild(tabNode, statNode);
                }
            }
        }

        private static void BuildStatDetailChildren(InspectionTreeItem statNode, StatDrawEntry entry)
        {
            if (statNode.Children.Count > 0)
                return;

            try
            {
                string explanation = entry.GetExplanationText(StatRequest.ForEmpty());
                if (!string.IsNullOrEmpty(explanation))
                {
                    explanation = explanation.StripTags();
                    var lines = explanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    // Skip lines that are redundant with the entry label
                    // (e.g., "Required apparel" as a header line)
                    string normalizedEntryLabel = entry.LabelCap.ToString().TrimEnd(':', '.', ' ').ToLowerInvariant();

                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (!string.IsNullOrEmpty(trimmedLine))
                        {
                            string normalizedLine = trimmedLine.TrimEnd(':', '.', ' ').ToLowerInvariant();
                            if (normalizedLine == normalizedEntryLabel)
                                continue; // Skip redundant header line

                            AddChild(statNode, CreateInfoItem(trimmedLine, statNode.IndentLevel + 1));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardTreeBuilder] Error building stat details: {ex.Message}");
                AddChild(statNode, CreateInfoItem("Unable to load details", statNode.IndentLevel + 1));
            }
        }

        private static void BuildXenotypeGeneChildren(InspectionTreeItem statNode, XenotypeDef xenoDef)
        {
            if (statNode.Children.Count > 0) return;

            foreach (var geneDef in xenoDef.genes)
            {
                string geneLabel = geneDef.LabelCap;
                if (!string.IsNullOrEmpty(geneDef.description))
                    geneLabel += ". " + geneDef.description;

                var geneNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = geneLabel,
                    Data = geneDef,
                    IsExpandable = false,
                    IsExpanded = false,
                    IndentLevel = statNode.IndentLevel + 1
                };
                AddChild(statNode, geneNode);
            }

            if (statNode.Children.Count == 0)
                AddChild(statNode, CreateInfoItem("No genes", statNode.IndentLevel + 1));
        }

        #endregion

        #region Character Tab

        private static void BuildCharacterTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                AddChild(tabNode, CreateInfoItem("No character data available", tabNode.IndentLevel + 1));
                return;
            }

            // Backstory
            var backstoryInfo = InfoCardDataExtractor.GetBackstoryInfo(pawn);
            if (backstoryInfo.Count > 0)
            {
                foreach (var (title, description) in backstoryInfo)
                {
                    bool hasDescription = !string.IsNullOrEmpty(description);
                    string storyLabel = title;
                    string storyExpandedLabel = null;
                    if (hasDescription)
                    {
                        storyExpandedLabel = title;
                        storyLabel = title + ". " + description.StripTags();
                    }

                    var storyNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = storyLabel,
                        ExpandedLabel = storyExpandedLabel,
                        Description = "Backstory",
                        IsExpandable = hasDescription,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasDescription)
                    {
                        storyNode.OnActivate = () =>
                        {
                            if (storyNode.Children.Count > 0) return;
                            var lines = description.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                AddChild(storyNode, CreateInfoItem(line.Trim(), storyNode.IndentLevel + 1));
                            }
                        };
                    }
                    AddChild(tabNode, storyNode);
                }
            }

            // Traits
            var traitsInfo = InfoCardDataExtractor.GetTraitsInfo(pawn);
            if (traitsInfo.Count > 0)
            {
                foreach (var (label, description, suppressed) in traitsInfo)
                {
                    string displayLabel = suppressed ? $"{label} (Suppressed)" : label;
                    bool hasTraitDescription = !string.IsNullOrEmpty(description);
                    string traitExpandedLabel = null;
                    if (hasTraitDescription)
                    {
                        traitExpandedLabel = displayLabel;
                        displayLabel = displayLabel + ". " + description.StripTags();
                    }

                    var traitNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = displayLabel,
                        ExpandedLabel = traitExpandedLabel,
                        Description = "Traits",
                        IsExpandable = hasTraitDescription,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasTraitDescription)
                    {
                        traitNode.OnActivate = () =>
                        {
                            if (traitNode.Children.Count > 0) return;
                            var lines = description.StripTags().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                AddChild(traitNode, CreateInfoItem(line.Trim(), traitNode.IndentLevel + 1));
                            }
                        };
                    }
                    AddChild(tabNode, traitNode);
                }
            }

            // Skills
            var skillsInfo = InfoCardDataExtractor.GetSkillsInfo(pawn);
            if (skillsInfo.Count > 0)
            {
                foreach (var (def, level, passion, disabled, levelDesc) in skillsInfo)
                {
                    string passionStr = "";
                    if (passion == Passion.Minor)
                        passionStr = " (Minor passion)";
                    else if (passion == Passion.Major)
                        passionStr = " (Major passion)";

                    string label = disabled
                        ? $"{def.skillLabel.CapitalizeFirst()}: Disabled"
                        : $"{def.skillLabel.CapitalizeFirst()}: {level}{passionStr} ({levelDesc})";

                    var skillNode = CreateInfoItem(label, tabNode.IndentLevel + 1);
                    skillNode.Description = "Skills";
                    AddChild(tabNode, skillNode);
                }
            }

            // Incapable Of - organized by WorkTag with inline causes, expandable to show affected work types
            var incapableTagsInfo = InfoCardDataExtractor.GetIncapableWorkTagsInfo(pawn);
            if (incapableTagsInfo.Count == 0)
            {
                var noneNode = CreateInfoItem("None".Translate(), tabNode.IndentLevel + 1);
                noneNode.Description = "Incapable Of";
                AddChild(tabNode, noneNode);
            }
            else
            {
                foreach (var (tagLabel, affectedWorkTypes) in incapableTagsInfo)
                {
                    bool hasWorkTypes = affectedWorkTypes.Count > 0;

                    // Aggregate work type names into Label for collapsed view
                    string incapableLabel = tagLabel;
                    string incapableExpandedLabel = null;
                    if (hasWorkTypes)
                    {
                        incapableExpandedLabel = tagLabel;
                        string workTypeNames = string.Join(", ", affectedWorkTypes.Select(w => w.pawnLabel));
                        incapableLabel = tagLabel + ". " + workTypeNames;
                    }

                    var tagNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = incapableLabel,
                        ExpandedLabel = incapableExpandedLabel,
                        Description = "Incapable Of",
                        IsExpandable = hasWorkTypes,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasWorkTypes)
                    {
                        var capturedWorkTypes = affectedWorkTypes;

                        tagNode.OnActivate = () =>
                        {
                            if (tagNode.Children.Count > 0) return;

                            foreach (var workTypeDef in capturedWorkTypes)
                            {
                                string label = !string.IsNullOrEmpty(workTypeDef.description)
                                    ? workTypeDef.pawnLabel + ": " + workTypeDef.description
                                    : workTypeDef.pawnLabel;
                                AddChild(tagNode, CreateInfoItem(label, tagNode.IndentLevel + 1));
                            }
                        };
                    }

                    AddChild(tabNode, tagNode);
                }
            }

            // Royal Titles
            var titlesInfo = InfoCardDataExtractor.GetRoyalTitlesInfo(pawn);
            if (titlesInfo.Count > 0)
            {
                foreach (var (title, faction, description) in titlesInfo)
                {
                    string titleShortLabel = $"{title} ({faction})";
                    bool hasTitleDescription = !string.IsNullOrEmpty(description);
                    string titleExpandedLabel = null;
                    string titleLabel = titleShortLabel;
                    if (hasTitleDescription)
                    {
                        titleExpandedLabel = titleShortLabel;
                        titleLabel = titleShortLabel + ". " + description.StripTags();
                    }

                    var titleNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = titleLabel,
                        ExpandedLabel = titleExpandedLabel,
                        Description = "Royal Titles",
                        IsExpandable = hasTitleDescription,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasTitleDescription)
                    {
                        titleNode.OnActivate = () =>
                        {
                            if (titleNode.Children.Count > 0) return;
                            AddChild(titleNode, CreateInfoItem(description.StripTags(), titleNode.IndentLevel + 1));
                        };
                    }
                    AddChild(tabNode, titleNode);
                }
            }

            // Ideology Role
            var roleInfo = InfoCardDataExtractor.GetIdeologyRoleInfo(pawn);
            if (roleInfo.HasValue)
            {
                var roleNode = CreateInfoItem($"{roleInfo.Value.roleName} ({roleInfo.Value.ideoName})", tabNode.IndentLevel + 1);
                roleNode.Description = "Ideology Role";
                AddChild(tabNode, roleNode);
            }

            // Abilities
            var abilitiesInfo = InfoCardDataExtractor.GetAbilitiesInfo(pawn);
            if (abilitiesInfo.Count > 0)
            {
                foreach (var (label, description) in abilitiesInfo)
                {
                    bool hasAbilityDescription = !string.IsNullOrEmpty(description);
                    string abilityLabel = label;
                    string abilityExpandedLabel = null;
                    if (hasAbilityDescription)
                    {
                        abilityExpandedLabel = label;
                        abilityLabel = label + ". " + description.StripTags();
                    }

                    var abilityNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = abilityLabel,
                        ExpandedLabel = abilityExpandedLabel,
                        Description = "Abilities",
                        IsExpandable = hasAbilityDescription,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasAbilityDescription)
                    {
                        abilityNode.OnActivate = () =>
                        {
                            if (abilityNode.Children.Count > 0) return;
                            AddChild(abilityNode, CreateInfoItem(description.StripTags(), abilityNode.IndentLevel + 1));
                        };
                    }
                    AddChild(tabNode, abilityNode);
                }
            }

            // Xenotype
            var xenotypeInfo = InfoCardDataExtractor.GetXenotypeInfo(pawn);
            if (xenotypeInfo.HasValue)
            {
                bool xenoExpandable = xenotypeInfo.Value.genes.Count > 0 || !string.IsNullOrEmpty(xenotypeInfo.Value.description);
                string xenoShortLabel = xenotypeInfo.Value.xenotypeName;
                string xenoLabel = xenoShortLabel;
                string xenoExpandedLabel = null;
                if (xenoExpandable)
                {
                    xenoExpandedLabel = xenoShortLabel;
                    var xenoParts = new List<string>();
                    if (!string.IsNullOrEmpty(xenotypeInfo.Value.description))
                        xenoParts.Add(xenotypeInfo.Value.description.StripTags());
                    if (xenotypeInfo.Value.genes.Count > 0)
                        xenoParts.Add(string.Join(", ", xenotypeInfo.Value.genes.Select(g => g.name)));
                    if (xenoParts.Count > 0)
                        xenoLabel = xenoShortLabel + ". " + string.Join(". ", xenoParts);
                }

                var xenoNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = xenoLabel,
                    ExpandedLabel = xenoExpandedLabel,
                    Description = "Xenotype",
                    IsExpandable = xenoExpandable,
                    IsExpanded = false,
                    IndentLevel = tabNode.IndentLevel + 1
                };

                xenoNode.OnActivate = () =>
                {
                    if (xenoNode.Children.Count > 0) return;
                    if (!string.IsNullOrEmpty(xenotypeInfo.Value.description))
                    {
                        AddChild(xenoNode, CreateInfoItem(xenotypeInfo.Value.description.StripTags(), xenoNode.IndentLevel + 1));
                    }
                    foreach (var (name, def) in xenotypeInfo.Value.genes)
                    {
                        var geneNode = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = $"Gene: {name}",
                            IsExpandable = false,
                            IsExpanded = false,
                            IndentLevel = xenoNode.IndentLevel + 1
                        };
                        AddChild(xenoNode, geneNode);
                    }
                };
                AddChild(tabNode, xenoNode);
            }

            // Favorite color (Ideology DLC)
            if (ModsConfig.IdeologyActive && !pawn.DevelopmentalStage.Baby() && pawn.story?.favoriteColor != null)
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
                var colorNode = CreateInfoItem(colorLabel, tabNode.IndentLevel + 1);
                colorNode.Description = "Favorite Color";
                AddChild(tabNode, colorNode);
            }
        }

        #endregion

        #region Health Tab

        private static void BuildHealthTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                AddChild(tabNode, CreateInfoItem("No health data available", tabNode.IndentLevel + 1));
                return;
            }

            // Capacities
            var capacitiesInfo = InfoCardDataExtractor.GetCapacitiesInfo(pawn);
            if (capacitiesInfo.Count > 0)
            {
                foreach (var (label, efficiency, tip) in capacitiesInfo)
                {
                    string efficiencyStr = efficiency.ToStringPercent();
                    string displayLabel = $"{label}: {efficiencyStr}";
                    bool hasCapTip = !string.IsNullOrEmpty(tip);
                    string capExpandedLabel = null;
                    if (hasCapTip)
                    {
                        capExpandedLabel = displayLabel;
                        string cleanTip = tip.StripTags();
                        var tipLines = cleanTip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        var trimmedLines = tipLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));
                        string aggregated = string.Join(". ", trimmedLines);
                        if (!string.IsNullOrEmpty(aggregated))
                            displayLabel = capExpandedLabel + ". " + aggregated;
                    }

                    var capacityNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = displayLabel,
                        ExpandedLabel = capExpandedLabel,
                        Description = "Capacities",
                        IsExpandable = hasCapTip,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasCapTip)
                    {
                        capacityNode.OnActivate = () =>
                        {
                            if (capacityNode.Children.Count > 0) return;
                            var lines = tip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                string trimmedLine = line.Trim().StripTags();
                                if (!string.IsNullOrEmpty(trimmedLine))
                                    AddChild(capacityNode, CreateInfoItem(trimmedLine, capacityNode.IndentLevel + 1));
                            }
                        };
                    }
                    AddChild(tabNode, capacityNode);
                }
            }

            // Conditions (hediffs)
            var hediffsInfo = InfoCardDataExtractor.GetHediffsInfo(pawn);
            if (hediffsInfo.Count > 0)
            {
                foreach (var (label, partLabel, severity, tip) in hediffsInfo)
                {
                    string displayLabel = string.IsNullOrEmpty(severity)
                        ? $"{partLabel}: {label}"
                        : $"{partLabel}: {label}: {severity}";

                    bool hasHediffTip = !string.IsNullOrEmpty(tip);
                    string hediffExpandedLabel = null;
                    if (hasHediffTip)
                    {
                        hediffExpandedLabel = displayLabel;
                        string cleanTip = tip.StripTags();
                        var tipLines = cleanTip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        string aggregated = string.Join(". ", tipLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));
                        if (!string.IsNullOrEmpty(aggregated))
                            displayLabel = hediffExpandedLabel + ". " + aggregated;
                    }

                    var hediffNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = displayLabel,
                        ExpandedLabel = hediffExpandedLabel,
                        Description = "Conditions",
                        IsExpandable = hasHediffTip,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasHediffTip)
                    {
                        hediffNode.OnActivate = () =>
                        {
                            if (hediffNode.Children.Count > 0) return;
                            var lines = tip.StripTags().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                AddChild(hediffNode, CreateInfoItem(line.Trim(), hediffNode.IndentLevel + 1));
                            }
                        };
                    }
                    AddChild(tabNode, hediffNode);
                }
            }
            else
            {
                var noConditionsNode = CreateInfoItem("No health conditions", tabNode.IndentLevel + 1);
                noConditionsNode.Description = "Conditions";
                AddChild(tabNode, noConditionsNode);
            }
        }

        #endregion

        #region Records Tab

        private static void BuildRecordsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                AddChild(tabNode, CreateInfoItem("No records available", tabNode.IndentLevel + 1));
                return;
            }

            // Time records
            var timeRecords = InfoCardDataExtractor.GetTimeRecords(pawn);
            if (timeRecords.Count > 0)
            {
                foreach (var (label, value) in timeRecords)
                {
                    var recordNode = CreateInfoItem($"{label}: {value}", tabNode.IndentLevel + 1);
                    recordNode.Description = "Time Records";
                    AddChild(tabNode, recordNode);
                }
            }

            // Misc records
            var miscRecords = InfoCardDataExtractor.GetMiscRecords(pawn);
            if (miscRecords.Count > 0)
            {
                foreach (var (label, value) in miscRecords)
                {
                    var recordNode = CreateInfoItem($"{label}: {value}", tabNode.IndentLevel + 1);
                    recordNode.Description = "Miscellaneous";
                    AddChild(tabNode, recordNode);
                }
            }

            if (timeRecords.Count == 0 && miscRecords.Count == 0)
            {
                AddChild(tabNode, CreateInfoItem("No records yet", tabNode.IndentLevel + 1));
            }
        }

        #endregion

        #region Permits Tab

        private static void BuildPermitsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null || !ModsConfig.RoyaltyActive || pawn.royalty == null)
            {
                AddChild(tabNode, CreateInfoItem("No permits available", tabNode.IndentLevel + 1));
                return;
            }

            var permitsInfo = InfoCardDataExtractor.GetPermitsInfo(pawn);
            if (permitsInfo.Count == 0)
            {
                AddChild(tabNode, CreateInfoItem("No permits available", tabNode.IndentLevel + 1));
                return;
            }

            // Group by faction
            var grouped = permitsInfo.GroupBy(p => p.faction);
            foreach (var group in grouped)
            {
                var faction = group.Key;
                string factionSectionName = faction.Name;

                // Title / points / favor info
                var currentTitle = pawn.royalty.GetCurrentTitle(faction);
                string titleLabelStr = currentTitle != null
                    ? currentTitle.GetLabelFor(pawn).CapitalizeFirst()
                    : (string)"None".Translate();
                var titleInfoNode = CreateInfoItem(
                    $"{"CurrentTitle".Translate()}: {titleLabelStr}",
                    tabNode.IndentLevel + 1);
                titleInfoNode.Description = factionSectionName;
                AddChild(tabNode, titleInfoNode);

                int permitPoints = pawn.royalty.GetPermitPoints(faction);
                var pointsNode = CreateInfoItem(
                    $"{"UnusedPermits".Translate()}: {permitPoints}",
                    tabNode.IndentLevel + 1);
                pointsNode.Description = factionSectionName;
                AddChild(tabNode, pointsNode);

                if (!faction.def.royalFavorLabel.NullOrEmpty())
                {
                    int favor = pawn.royalty.GetFavor(faction);
                    var favorNode = CreateInfoItem(
                        $"{faction.def.royalFavorLabel.CapitalizeFirst()}: {favor}",
                        tabNode.IndentLevel + 1);
                    favorNode.Description = factionSectionName;
                    AddChild(tabNode, favorNode);
                }

                // "Return all permits" action
                if (faction.def.HasRoyalTitles)
                {
                    int returnCost = InfoCardDataExtractor.TotalReturnPermitsCost(pawn);
                    string favorLabel = faction.def.royalFavorLabel.NullOrEmpty()
                        ? "favor" : faction.def.royalFavorLabel;

                    var returnNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Action,
                        Label = "ReturnAllPermits".Translate() + $" ({returnCost} {favorLabel})",
                        Description = factionSectionName,
                        IsExpandable = false,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    var capturedFaction = faction;
                    var capturedPawn = pawn;
                    var capturedTabNode = tabNode;
                    var capturedDialog = dialog;

                    returnNode.OnActivate = () =>
                    {
                        if (!capturedPawn.royalty.PermitsFromFaction(capturedFaction).Any())
                        {
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            TolkHelper.Speak(
                                "NoPermitsToReturn".Translate(capturedPawn.Named("PAWN")).Resolve(),
                                SpeechPriority.High);
                            return;
                        }

                        int cost = InfoCardDataExtractor.TotalReturnPermitsCost(capturedPawn);
                        int currentFavor = capturedPawn.royalty.GetFavor(capturedFaction);
                        if (currentFavor < cost)
                        {
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            TolkHelper.Speak(
                                "NotEnoughFavor".Translate(
                                    cost.Named("FAVORCOST"),
                                    capturedFaction.def.royalFavorLabel.Named("FAVOR"),
                                    capturedPawn.Named("PAWN"),
                                    currentFavor.Named("CURFAVOR")
                                ).Resolve(),
                                SpeechPriority.High);
                            return;
                        }

                        int baseCost = 8;
                        string confirmText = "ReturnAllPermits_Confirm".Translate(
                            baseCost.Named("BASEFAVORCOST"),
                            cost.Named("FAVORCOST"),
                            capturedFaction.def.royalFavorLabel.Named("FAVOR"),
                            capturedFaction.Named("FACTION")
                        ).Resolve();

                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, delegate
                        {
                            capturedPawn.royalty.RefundPermits(baseCost, capturedFaction);
                            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                            TolkHelper.Speak(
                                "ReturnAllPermits".Translate() + ". " +
                                "UnusedPermits".Translate() + ": " +
                                capturedPawn.royalty.GetPermitPoints(capturedFaction),
                                SpeechPriority.High);
                            RebuildPermitsTab(capturedTabNode, capturedDialog);
                        }, destructive: true));
                    };

                    AddChild(tabNode, returnNode);
                }

                // Individual permit nodes
                foreach (var (permitName, _, status, description, requiredTitle, def) in group)
                {
                    string label = $"{permitName} - {status}";
                    bool isUnlocked = InfoCardDataExtractor.IsPermitUnlocked(def, pawn, faction);
                    bool isAvailable = def.AvailableForPawn(pawn, faction) && !isUnlocked;

                    // Always expandable - permits have details (description, cooldown, requirements)
                    bool hasDetails = !string.IsNullOrEmpty(description) || def.minTitle != null ||
                                      def.prerequisite != null || def.cooldownDays > 0 || isAvailable;

                    // Aggregate description into Label for collapsed view
                    string permitExpandedLabel = null;
                    if (hasDetails && !string.IsNullOrEmpty(description))
                    {
                        permitExpandedLabel = label;
                        label = label + ". " + description.StripTags();
                    }

                    var permitNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        ExpandedLabel = permitExpandedLabel,
                        Description = factionSectionName,
                        IsExpandable = hasDetails,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasDetails)
                    {
                        var capturedDef = def;
                        var capturedFaction = faction;
                        var capturedPawn = pawn;
                        var capturedTabNode = tabNode;
                        var capturedDialog = dialog;
                        var capturedDescription = description;

                        permitNode.OnActivate = () =>
                        {
                            if (permitNode.Children.Count > 0) return;

                            // Required title
                            if (capturedDef.minTitle != null)
                            {
                                var curTitle = capturedPawn.royalty.GetCurrentTitle(capturedFaction);
                                bool titleMet = curTitle != null && curTitle.seniority >= capturedDef.minTitle.seniority;
                                string titleStatus = titleMet ? "" : " (not met)";
                                AddChild(permitNode, CreateInfoItem(
                                    "RequiresTitle".Translate(capturedDef.minTitle.GetLabelForBothGenders()).Resolve() + titleStatus,
                                    permitNode.IndentLevel + 1));
                            }

                            // Prerequisite
                            if (capturedDef.prerequisite != null)
                            {
                                bool prereqMet = InfoCardDataExtractor.IsPermitUnlocked(
                                    capturedDef.prerequisite, capturedPawn, capturedFaction);
                                string prereqStatus = prereqMet ? "" : " (not met)";
                                AddChild(permitNode, CreateInfoItem(
                                    "UpgradeFrom".Translate(capturedDef.prerequisite.LabelCap).Resolve() + prereqStatus,
                                    permitNode.IndentLevel + 1));
                            }

                            // Cooldown
                            if (capturedDef.cooldownDays > 0)
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    "Cooldown".Translate() + ": " + "PeriodDays".Translate(capturedDef.cooldownDays),
                                    permitNode.IndentLevel + 1));
                            }

                            // Favor cost if used during cooldown
                            if (capturedDef.royalAid != null && capturedDef.royalAid.favorCost > 0 &&
                                !capturedFaction.def.royalFavorLabel.NullOrEmpty())
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    "CooldownUseFavorCost".Translate(
                                        capturedFaction.def.royalFavorLabel.Named("HONOR")
                                    ).CapitalizeFirst().Resolve() + ": " + capturedDef.royalAid.favorCost,
                                    permitNode.IndentLevel + 1));
                            }

                            // Description
                            if (!string.IsNullOrEmpty(capturedDescription))
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    capturedDescription.StripTags(), permitNode.IndentLevel + 1));
                            }

                            // "Accept permit" action (only if currently available)
                            bool currentlyAvailable = capturedDef.AvailableForPawn(capturedPawn, capturedFaction)
                                && !InfoCardDataExtractor.IsPermitUnlocked(capturedDef, capturedPawn, capturedFaction);

                            if (currentlyAvailable)
                            {
                                var acceptNode = new InspectionTreeItem
                                {
                                    Type = InspectionTreeItem.ItemType.Action,
                                    Label = "AcceptPermit".Translate(),
                                    IsExpandable = false,
                                    IsExpanded = false,
                                    IndentLevel = permitNode.IndentLevel + 1
                                };

                                acceptNode.OnActivate = () =>
                                {
                                    // Re-validate at activation time
                                    if (!capturedDef.AvailableForPawn(capturedPawn, capturedFaction))
                                    {
                                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                                        TolkHelper.Speak("Permit no longer available.", SpeechPriority.High);
                                        return;
                                    }

                                    capturedPawn.royalty.AddPermit(capturedDef, capturedFaction);
                                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();

                                    int remainingPoints = capturedPawn.royalty.GetPermitPoints(capturedFaction);
                                    TolkHelper.Speak(
                                        capturedDef.LabelCap + " granted. " +
                                        "UnusedPermits".Translate() + ": " + remainingPoints,
                                        SpeechPriority.High);

                                    RebuildPermitsTab(capturedTabNode, capturedDialog);
                                };

                                AddChild(permitNode, acceptNode);
                            }
                        };
                    }

                    AddChild(tabNode, permitNode);
                }
            }
        }

        private static void RebuildPermitsTab(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            tabNode.Children.Clear();
            BuildPermitsTabChildren(tabNode, dialog);
            tabNode.IsExpanded = true;
            InfoCardState.RefreshVisibleListAndAnnounce();
        }

        #endregion

        #region Helpers

        private static InspectionTreeItem CreateInfoItem(string label, int indent)
        {
            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = label,
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = indent
            };
        }

        private static InspectionTreeItem CreateCategoryHeader(string label, int indent)
        {
            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = indent
            };
        }

        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            // Children inherit parent's section (Description) for section tracking.
            // This prevents section re-announcement when drilling into children and back.
            if (string.IsNullOrEmpty(child.Description) && !string.IsNullOrEmpty(parent.Description))
                child.Description = parent.Description;
            parent.Children.Add(child);
        }

        /// <summary>
        /// Determines if a pawn can be renamed by the player.
        /// </summary>
        private static bool CanPawnBeRenamed(Pawn pawn)
        {
            if (pawn == null) return false;

            // Colonists and colony subhumans can be renamed
            if (pawn.IsColonist || pawn.IsColonySubhuman) return true;

            // Animals and mechs belonging to player can be renamed
            if (pawn.Faction == Faction.OfPlayer &&
                (pawn.RaceProps.Animal || pawn.RaceProps.IsMechanoid))
                return true;

            return false;
        }

        /// <summary>
        /// Returns true if we're in a context where opening additional dialogs would cause conflicts.
        /// </summary>
        private static bool IsInModalContext()
        {
            // Check for states that manage modal dialogs
            if (CaravanFormationState.IsActive) return true;
            if (SplitCaravanState.IsActive) return true;
            if (TradeNavigationState.IsActive) return true;
            if (TransportPodLoadingState.IsActive) return true;
            if (RitualState.IsActive) return true;

            return false;
        }

        #endregion

        #region Actions Tab

        /// <summary>
        /// Builds children for the Actions tab.
        /// </summary>
        private static void BuildActionsTabChildren(InspectionTreeItem tabNode, Pawn pawn)
        {
            if (tabNode.Children.Count > 0) return; // Already built

            // Add Rename action
            var renameItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "Rename",
                Data = pawn,
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = tabNode.IndentLevel + 1
            };

            renameItem.OnActivate = () =>
            {
                // Close Info Card before opening rename dialog
                InfoCardState.CloseInfoCard();

                // Open Dialog_NamePawn - DialogInterceptionPatch will make it accessible
                Find.WindowStack.Add(pawn.NamePawnDialog());
            };

            AddChild(tabNode, renameItem);
        }

        #endregion
    }
}
