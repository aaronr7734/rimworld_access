using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds InspectionTreeItem trees from Dialog_InfoCard data, for every tab the card offers.
    /// </summary>
    public static class InfoCardTreeBuilder
    {
        /// <summary>Builds the complete tree for an info card, with all available tabs.</summary>
        public static InspectionTreeItem BuildTree(Dialog_InfoCard dialog)
        {
            bool vehicleCard = VfInfoCardCompat.OwnsCard(dialog);

            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = vehicleCard ? GetVehicleRootLabel(dialog) : GetRootLabel(dialog),
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            var availableTabs = vehicleCard
                ? VehicleCardTabs()
                : InfoCardDataExtractor.GetAvailableTabs(dialog);

            if (availableTabs.Count == 1)
            {
                BuildTabChildren(root, dialog, availableTabs[0]);
                return root;
            }

            foreach (var tab in availableTabs)
            {
                var tabNode = CreateTabNode(dialog, tab);
                AddChild(root, tabNode);
            }

            // Add Actions tab if pawn has available actions (but not in modal contexts).
            // Never on a Vehicle Framework card: it draws no such button, and a vehicle is
            // renamed through VF's own Dialog_GiveVehicleName, not vanilla's NamePawnDialog.
            var thing = InfoCardDataExtractor.GetThing(dialog);
            if (!vehicleCard && thing is Pawn pawn && PawnRenameHelper.CanRename(pawn) && !IsInModalContext())
            {
                var actionsTab = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "RimWorldAccess.Inspection.InfoCardTree.ActionsTab".Translate(),
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

        /// <summary>Gets the root label for the info card based on what's being displayed.</summary>
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
        /// The title VF's own card prints, which for a placeholder building or a build def is
        /// the vehicle's label rather than the thing's. Falls back to the vanilla label when VF
        /// has no target yet.
        /// </summary>
        private static string GetVehicleRootLabel(Dialog_InfoCard dialog)
        {
            string title = VfInfoCardCompat.CardTitle();
            if (string.IsNullOrEmpty(title))
                return GetRootLabel(dialog);

            return $"{ConceptDefOf.InfoCard.label.CapitalizeFirst()}: {title}";
        }

        /// <summary>
        /// The three tabs VF's card builds (VehicleInfoCard.Draw), with the vanilla labels and
        /// enum values it uses: no Character and no Permits, whatever the pawn would qualify for.
        /// </summary>
        private static List<Dialog_InfoCard.InfoCardTab> VehicleCardTabs()
        {
            return new List<Dialog_InfoCard.InfoCardTab>
            {
                Dialog_InfoCard.InfoCardTab.Stats,
                Dialog_InfoCard.InfoCardTab.Health,
                Dialog_InfoCard.InfoCardTab.Records
            };
        }

        /// <summary>Creates a tab node with lazy-loaded children.</summary>
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

            tabNode.OnActivate = () =>
            {
                dialog.SetTab(tab);
                BuildTabChildren(tabNode, dialog, tab);
            };

            return tabNode;
        }

        /// <summary>Gets the display label for a tab.</summary>
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

        /// <summary>Builds children for a specific tab.</summary>
        private static void BuildTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog, Dialog_InfoCard.InfoCardTab tab)
        {
            if (tabNode.Children.Count > 0)
                return; // Already built

            if (VfInfoCardCompat.OwnsCard(dialog))
            {
                BuildVehicleTabChildren(tabNode, tab);
                return;
            }

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

            // Lazy-loaded nodes set ExpandedLabel/Label inline at creation: BuildSmartLabels
            // cannot work on nodes whose children have not been populated yet.
        }

        #region Stats Tab

        private static void BuildStatsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var entries = InfoCardDataExtractor.GetStatEntries();
            if (entries.Count == 0)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoStats".Translate());
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
                // The category name rides in Description for section announcements. Ordering
                // matches vanilla's StatsReportUtility.FinalizeCachedDrawEntries, so equal-priority
                // stats break ties in the order sighted players see.
                var sortedEntries = group.Entries
                    .OrderByDescending(e => e.DisplayPriorityWithinCategory)
                    .ThenBy(e => e.LabelCap);
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
                            // Skip lines that just repeat the entry label (e.g., "Required apparel:" header)
                            value = InspectTextUtility.SplitLines(explanation, entry.LabelCap.ToString()).FirstOrDefault();
                        }
                        catch { }
                    }

                    string entryLabel = entry.LabelCap.ToString();
                    // RimWorld sometimes returns a value matching the label plus punctuation.
                    if (!emptyValue && InspectTextUtility.IsRedundantWith(value, entryLabel))
                    {
                        // Value is redundant with label — treat as empty and re-extract from explanation
                        emptyValue = true;
                        value = null;
                        try
                        {
                            string explanation = entry.GetExplanationText(StatRequest.ForEmpty())?.Trim();
                            value = InspectTextUtility.SplitLines(explanation, entryLabel).FirstOrDefault();
                        }
                        catch { }
                    }

                    string label;
                    if (string.IsNullOrEmpty(value))
                        label = entryLabel;
                    else
                        label = $"{entryLabel}: {value}";

                    // Shade-aware gene labels for GeneSetHolderBase items; the explanation is
                    // suppressed because the enriched label already carries every gene name.
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

                    // Empty-value entries are skipped: their label is the explanation text, and
                    // their hyperlinks stay reachable via Alt+I.
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
                                    // Mirrors vanilla's own Hyperlink.Label across every link
                                    // shape, honoring the hidden-item substitution so an
                                    // undiscovered item's real name never leaks out here.
                                    string name = InfoCardDataExtractor.GetHyperlinkLabel(link);
                                    if (!string.IsNullOrEmpty(name) && !label.ToLower().Contains(name.ToLower()))
                                        defNames.Add(name.CapitalizeFirst());
                                }
                                if (defNames.Count > 0)
                                    label += $" ({string.Join(", ", defNames)})";
                            }
                        }
                        catch { }
                    }

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

                    // Some entries (an animal's sex, its category) pass the value as the report
                    // text, so the explanation says nothing the row has not already said.
                    if (hasExplanation && !emptyValue
                        && InspectTextUtility.SplitLines(explanationText.StripTags(), entryLabel)
                            .All(line => InspectTextUtility.IsRedundantWith(line, value)))
                    {
                        hasExplanation = false;
                        explanationText = null;
                    }

                    if (emptyValue && !hasExplanation)
                        continue;

                    // Expandable nodes carry the short form in ExpandedLabel and the aggregated
                    // form in Label. Empty-value stats already hold the explanation as their
                    // value, so it must not be aggregated twice.
                    string statExpandedLabel = null;
                    if (hasExplanation && explanationText != null)
                    {
                        if (emptyValue)
                        {
                            // Empty-value stat: aggregate ALL non-redundant explanation lines
                            statExpandedLabel = entry.LabelCap.ToString();
                            string aggregated = string.Join(". ",
                                InspectTextUtility.SplitLines(explanationText.StripTags(), entryLabel));
                            if (!string.IsNullOrEmpty(aggregated))
                                label = statExpandedLabel + ": " + aggregated;
                        }
                        else
                        {
                            // Real value: ExpandedLabel is "StatName: Value", Label appends the
                            // explanation.
                            statExpandedLabel = label;
                            string aggregated = string.Join(". ",
                                InspectTextUtility.SplitLines(explanationText.StripTags(), entryLabel));
                            if (!string.IsNullOrEmpty(aggregated))
                                label = statExpandedLabel + ". " + aggregated;
                        }
                    }

                    var statNode = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = label,
                        ExpandedLabel = statExpandedLabel,
                        Description = group.Label,
                        // Empty-value rows get a marker datum instead of the entry so the
                        // Alt+I hyperlink walk passes over them (see EmptyValueStatDatum).
                        Data = emptyValue ? new EmptyValueStatDatum(entry) : (object)entry,
                        IsExpandable = hasExplanation,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    if (hasExplanation)
                    {
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
                    // Skip lines redundant with the entry label (header lines).
                    foreach (string line in InspectTextUtility.SplitLines(
                        explanation.StripTags(), entry.LabelCap.ToString()))
                    {
                        AddChild(statNode, CreateInfoItem(line, statNode.IndentLevel + 1));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardTreeBuilder] Error building stat details: {ex.Message}");
                AddChild(statNode, CreateInfoItem("RimWorldAccess.Inspection.InfoCardTree.UnableToLoadDetails".Translate(), statNode.IndentLevel + 1));
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
                AddChild(statNode, CreateInfoItem("RimWorldAccess.Inspection.InfoCardTree.NoGenes".Translate(), statNode.IndentLevel + 1));
        }

        #endregion

        #region Vehicle Framework card

        /// <summary>
        /// Fills a tab of the card Vehicle Framework draws instead of vanilla's (see
        /// <see cref="VfInfoCardCompat"/>). Stats carries VF's own report; Health and Records are
        /// rendered blank by VF, so each says so rather than showing data no one else can see.
        /// </summary>
        private static void BuildVehicleTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard.InfoCardTab tab)
        {
            if (tab == Dialog_InfoCard.InfoCardTab.Stats)
            {
                BuildVehicleStatsChildren(tabNode);
                return;
            }

            InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Compat.Vf.InfoCardTabBlank".Translate());
        }

        private static void BuildVehicleStatsChildren(InspectionTreeItem tabNode)
        {
            var rows = VfInfoCardCompat.ReadStatRows();
            if (rows.Count == 0)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoStats".Translate());
                return;
            }

            // VF's own FinalizeCachedDrawEntries already ordered by category display order, so
            // same-category rows are contiguous and the category label is each row's section.
            foreach (var row in rows)
            {
                BuildVehicleStatRow(tabNode, row);
            }
        }

        /// <summary>
        /// One VF stat row: "label: value" collapsed with the explanation aggregated on, a short
        /// ExpandedLabel, and the explanation split into child rows when expanded; an empty value
        /// puts the explanation in the value slot.
        ///
        /// DEVIATION from the vanilla path: an empty-value row still carries its hyperlinks
        /// (vanilla masks them behind <see cref="EmptyValueStatDatum"/>), because VF's card draws
        /// the hyperlinks of whichever entry is selected, Description included.
        /// </summary>
        private static void BuildVehicleStatRow(InspectionTreeItem tabNode, VehicleStatRow row)
        {
            string entryLabel = row.Label;
            string value = row.Value;
            bool emptyValue = string.IsNullOrEmpty(value) || InspectTextUtility.IsRedundantWith(value, entryLabel);

            var explanationLines = InspectTextUtility.SplitLines(row.Explanation.StripTags(), entryLabel);
            bool hasExplanation = explanationLines.Count > 0;

            if (emptyValue && !hasExplanation)
                return;

            string label;
            string expandedLabel = null;
            if (emptyValue)
            {
                expandedLabel = entryLabel;
                label = expandedLabel + ": " + string.Join(". ", explanationLines);
            }
            else
            {
                label = $"{entryLabel}: {value}";

                var linkNames = new List<string>();
                foreach (var link in row.Hyperlinks)
                {
                    string name = InfoCardDataExtractor.GetHyperlinkLabel(link);
                    if (!string.IsNullOrEmpty(name) && !label.ToLower().Contains(name.ToLower()))
                        linkNames.Add(name.CapitalizeFirst());
                }
                if (linkNames.Count > 0)
                    label += $" ({string.Join(", ", linkNames)})";

                if (hasExplanation)
                {
                    expandedLabel = label;
                    label = expandedLabel + ". " + string.Join(". ", explanationLines);
                }
            }

            var statNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                ExpandedLabel = expandedLabel,
                Description = row.CategoryLabel,
                Data = row.Hyperlinks.Count > 0 ? row.Hyperlinks : null,
                IsExpandable = hasExplanation,
                IsExpanded = false,
                IndentLevel = tabNode.IndentLevel + 1
            };

            if (hasExplanation)
            {
                statNode.OnActivate = () =>
                {
                    if (statNode.Children.Count > 0) return;
                    foreach (string line in explanationLines)
                    {
                        AddChild(statNode, CreateInfoItem(line, statNode.IndentLevel + 1));
                    }
                };
            }

            AddChild(tabNode, statNode);
        }

        #endregion

        #region Character Tab

        private static void BuildCharacterTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoCharacterData".Translate());
                return;
            }

            // Vanilla shows the age in the card header with the birth date and
            // chronological/biological breakdown on hover.
            foreach (var ageLine in InfoCardDataExtractor.GetAgeInfo(pawn))
            {
                InspectNodeFactory.DetailLine(tabNode, ageLine);
            }

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
                        Description = "Backstory".Translate(),
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

            var traitsInfo = InfoCardDataExtractor.GetTraitsInfo(pawn);
            if (traitsInfo.Count > 0)
            {
                foreach (var (label, description, suppressed) in traitsInfo)
                {
                    string displayLabel = suppressed
                        ? label + (string)"RimWorldAccess.Inspection.InfoCardTree.SuppressedSuffix".Translate()
                        : label;
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
                        Description = "Traits".Translate(),
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

            var skillsInfo = InfoCardDataExtractor.GetSkillsInfo(pawn);
            if (skillsInfo.Count > 0)
            {
                foreach (var (def, level, passion, disabled, levelDesc) in skillsInfo)
                {
                    string passionStr = "";
                    if (passion == Passion.Minor)
                        passionStr = "RimWorldAccess.Inspection.InfoCardTree.MinorPassionSuffix".Translate();
                    else if (passion == Passion.Major)
                        passionStr = "RimWorldAccess.Inspection.InfoCardTree.MajorPassionSuffix".Translate();

                    string skillName = def.skillLabel.CapitalizeFirst();
                    string label = disabled
                        ? "RimWorldAccess.Inspection.InfoCardTree.SkillLabelDisabled".Translate(skillName, "Disabled".Translate())
                        : "RimWorldAccess.Inspection.InfoCardTree.SkillLabelLeveled".Translate(skillName, level, passionStr, levelDesc);

                    var skillNode = InspectNodeFactory.DetailLine(tabNode, label);
                    skillNode.Description = "Skills".Translate();
                }
            }

            var incapableTagsInfo = InfoCardDataExtractor.GetIncapableWorkTagsInfo(pawn);
            if (incapableTagsInfo.Count == 0)
            {
                var noneNode = InspectNodeFactory.DetailLine(tabNode, "None".Translate());
                noneNode.Description = "IncapableOf".Translate();
            }
            else
            {
                foreach (var (tagLabel, affectedWorkTypes) in incapableTagsInfo)
                {
                    bool hasWorkTypes = affectedWorkTypes.Count > 0;

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
                        Description = "IncapableOf".Translate(),
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

            var titlesInfo = InfoCardDataExtractor.GetRoyalTitlesInfo(pawn);
            if (titlesInfo.Count > 0)
            {
                var allTitles = pawn.royalty.AllTitlesForReading;
                for (int titleIndex = 0; titleIndex < titlesInfo.Count; titleIndex++)
                {
                    var (title, faction, description) = titlesInfo[titleIndex];
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
                        Description = "RimWorldAccess.Inspection.InfoCardTree.Section.RoyalTitles".Translate(),
                        IsExpandable = true,
                        IsExpanded = false,
                        IndentLevel = tabNode.IndentLevel + 1
                    };

                    // Captured now, while titlesInfo and allTitles are still index-aligned (both
                    // come from a single read of AllTitlesForReading at build time).
                    var royalTitle = allTitles[titleIndex];
                    var capturedTabNode = tabNode;
                    var capturedDialog = dialog;

                    titleNode.OnActivate = () =>
                    {
                        if (titleNode.Children.Count > 0) return;

                        if (hasTitleDescription)
                        {
                            AddChild(titleNode, CreateInfoItem(description.StripTags(), titleNode.IndentLevel + 1));
                        }

                        // Mirrors vanilla CharacterCardUtility's RenounceTitle button.
                        var renounceNode = InspectNodeFactory.ActionRow(titleNode, "RenounceTitle".Translate(), null,
                            () => RenounceRoyalTitle(pawn, royalTitle, capturedTabNode, capturedDialog));
                        // ActionRow does not propagate the section name the way AddChild does.
                        renounceNode.Description = titleNode.Description;
                    };
                    AddChild(tabNode, titleNode);
                }
            }

            // Ideology Role expands into the role's tip lines: collapsed speaks everything in
            // one utterance, expanded keeps the label short and moves the tip into child rows.
            var roleInfo = InfoCardDataExtractor.GetIdeologyRoleInfo(pawn);
            if (roleInfo.HasValue)
            {
                string roleShortLabel = $"{roleInfo.Value.roleName} ({roleInfo.Value.ideoName})";
                bool hasRoleTip = roleInfo.Value.tipLines.Count > 0;
                string roleLabel = roleShortLabel;
                string roleExpandedLabel = null;
                if (hasRoleTip)
                {
                    roleExpandedLabel = roleShortLabel;
                    roleLabel = roleShortLabel + ". " + string.Join(". ", roleInfo.Value.tipLines);
                }

                var roleNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = roleLabel,
                    ExpandedLabel = roleExpandedLabel,
                    Description = "RimWorldAccess.Inspection.InfoCardTree.Section.IdeologyRole".Translate(),
                    IsExpandable = hasRoleTip,
                    IsExpanded = false,
                    IndentLevel = tabNode.IndentLevel + 1
                };

                if (hasRoleTip)
                {
                    roleNode.OnActivate = () =>
                    {
                        if (roleNode.Children.Count > 0) return;
                        foreach (var line in roleInfo.Value.tipLines)
                        {
                            AddChild(roleNode, CreateInfoItem(line, roleNode.IndentLevel + 1));
                        }
                    };
                }
                AddChild(tabNode, roleNode);
            }

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
                        Description = "Abilities".Translate(),
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
                    Description = "Xenotype".Translate(),
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
                            Label = "RimWorldAccess.Inspection.InfoCardTree.GeneEntry".Translate(name),
                            IsExpandable = false,
                            IsExpanded = false,
                            IndentLevel = xenoNode.IndentLevel + 1
                        };
                        AddChild(xenoNode, geneNode);
                    }
                };
                AddChild(tabNode, xenoNode);
            }

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
                var colorNode = InspectNodeFactory.DetailLine(tabNode, colorLabel);
                colorNode.Description = "RimWorldAccess.Inspection.InfoCardTree.Section.FavoriteColor".Translate();
            }
        }

        private static void RebuildCharacterTab(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            tabNode.Children.Clear();
            BuildCharacterTabChildren(tabNode, dialog);
            tabNode.IsExpanded = true;
            InfoCardState.RefreshVisibleListAndAnnounce();
        }

        /// <summary>
        /// Renounces a royal title through vanilla's own vehicle: the same confirmation text and
        /// the same mutators (Pawn_RoyaltyTracker.SetTitle + ResetPermitsAndPoints) behind the
        /// same Dialog_MessageBox as CharacterCardUtility's RenounceTitle button.
        /// </summary>
        private static void RenounceRoyalTitle(Pawn pawn, RoyalTitle title, InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            List<FactionPermit> permitsFromFaction = pawn.royalty.PermitsFromFaction(title.faction);
            RoyalTitleUtility.FindLostAndGainedPermits(title.def, null, out _, out var lostPermits);

            RoyalTitleDef FirstTitleWithPermit(RoyalTitlePermitDef permitDef)
            {
                return title.faction.def.RoyalTitlesAwardableInSeniorityOrderForReading
                    .First(t => t.permits != null && t.permits.Contains(permitDef));
            }

            var stringBuilder = new StringBuilder();
            if (lostPermits.Count > 0 || permitsFromFaction.Count > 0)
            {
                stringBuilder.AppendLine("RenounceTitleWillLoosePermits".Translate(pawn.Named("PAWN")) + ":");
                foreach (var item in lostPermits)
                {
                    stringBuilder.AppendLine("- " + item.LabelCap + " (" + FirstTitleWithPermit(item).GetLabelFor(pawn) + ")");
                }
                foreach (var item2 in permitsFromFaction)
                {
                    stringBuilder.AppendLine("- " + item2.Permit.LabelCap + " (" + item2.Title.GetLabelFor(pawn) + ")");
                }
                stringBuilder.AppendLine();
            }

            int permitPoints = pawn.royalty.GetPermitPoints(title.faction);
            if (permitPoints > 0)
            {
                stringBuilder.AppendLineTagged("RenounceTitleWillLosePermitPoints".Translate(pawn.Named("PAWN"), permitPoints.Named("POINTS"), title.faction.Named("FACTION")));
            }
            if (pawn.abilities.abilities.Any())
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLineTagged("RenounceTitleWillKeepPsylinkLevels".Translate(pawn.Named("PAWN")));
            }
            if (!title.faction.def.renounceTitleMessage.NullOrEmpty())
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine(title.faction.def.renounceTitleMessage);
            }

            TaggedString titleOfFaction = "TitleOfFaction".Translate(title.def.GetLabelCapFor(pawn), title.faction.GetCallLabel());
            string confirmText = "RenounceTitleDescription".Translate(
                pawn.Named("PAWN"),
                titleOfFaction.Named("TITLE"),
                stringBuilder.ToString().TrimEndNewlines().Named("EFFECTS")
            ).Resolve();

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, delegate
            {
                pawn.royalty.SetTitle(title.faction, null, grantRewards: false);
                pawn.royalty.ResetPermitsAndPoints(title.faction, title.def);

                SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                TolkHelper.Speak(
                    "RimWorldAccess.Inspection.InfoCardTree.TitleRenounced".Loc(
                        "RenounceTitle".Translate(),
                        titleOfFaction),
                    SpeechPriority.High);

                RebuildCharacterTab(tabNode, dialog);
            }, destructive: true));
        }

        #endregion

        #region Health Tab

        private static void BuildHealthTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoHealthData".Translate());
                return;
            }

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
                        Description = "RimWorldAccess.Inspection.InfoCardTree.Section.Capacities".Translate(),
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
                        Description = "RimWorldAccess.Inspection.InfoCardTree.Section.Conditions".Translate(),
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
                var noConditionsNode = InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoHealthConditions".Translate());
                noConditionsNode.Description = "RimWorldAccess.Inspection.InfoCardTree.Section.Conditions".Translate();
            }
        }

        #endregion

        #region Records Tab

        private static void BuildRecordsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoRecordsAvailable".Translate());
                return;
            }

            var timeRecords = InfoCardDataExtractor.GetTimeRecords(pawn);
            if (timeRecords.Count > 0)
            {
                foreach (var (label, value) in timeRecords)
                {
                    var recordNode = InspectNodeFactory.DetailLine(tabNode, $"{label}: {value}");
                    recordNode.Description = "RimWorldAccess.Inspection.InfoCardTree.Section.TimeRecords".Translate();
                }
            }

            var miscRecords = InfoCardDataExtractor.GetMiscRecords(pawn);
            if (miscRecords.Count > 0)
            {
                foreach (var (label, value) in miscRecords)
                {
                    var recordNode = InspectNodeFactory.DetailLine(tabNode, $"{label}: {value}");
                    recordNode.Description = "RimWorldAccess.Inspection.InfoCardTree.Section.Miscellaneous".Translate();
                }
            }

            if (timeRecords.Count == 0 && miscRecords.Count == 0)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoRecordsYet".Translate());
            }
        }

        #endregion

        #region Permits Tab

        private static void BuildPermitsTabChildren(InspectionTreeItem tabNode, Dialog_InfoCard dialog)
        {
            var pawn = InfoCardDataExtractor.GetPawn(dialog);
            if (pawn == null || !ModsConfig.RoyaltyActive || pawn.royalty == null)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoPermitsAvailable".Translate());
                return;
            }

            var permitsInfo = InfoCardDataExtractor.GetPermitsInfo(pawn);
            if (permitsInfo.Count == 0)
            {
                InspectNodeFactory.DetailLine(tabNode, "RimWorldAccess.Inspection.InfoCardTree.NoPermitsAvailable".Translate());
                return;
            }

            var grouped = permitsInfo.GroupBy(p => p.faction);
            foreach (var group in grouped)
            {
                var faction = group.Key;
                string factionSectionName = faction.Name;

                var currentTitle = pawn.royalty.GetCurrentTitle(faction);
                string titleLabelStr = currentTitle != null
                    ? currentTitle.GetLabelFor(pawn).CapitalizeFirst()
                    : (string)"None".Translate();
                var titleInfoNode = InspectNodeFactory.DetailLine(tabNode,
                    $"{"CurrentTitle".Translate()}: {titleLabelStr}");
                titleInfoNode.Description = factionSectionName;

                int permitPoints = pawn.royalty.GetPermitPoints(faction);
                var pointsNode = InspectNodeFactory.DetailLine(tabNode,
                    $"{"UnusedPermits".Translate()}: {permitPoints}");
                pointsNode.Description = factionSectionName;

                if (!faction.def.royalFavorLabel.NullOrEmpty())
                {
                    int favor = pawn.royalty.GetFavor(faction);
                    var favorNode = InspectNodeFactory.DetailLine(tabNode,
                        $"{faction.def.royalFavorLabel.CapitalizeFirst()}: {favor}");
                    favorNode.Description = factionSectionName;
                }

                if (faction.def.HasRoyalTitles)
                {
                    int returnCost = InfoCardDataExtractor.TotalReturnPermitsCost(pawn);
                    string favorLabel = faction.def.royalFavorLabel.NullOrEmpty()
                        ? (string)"RimWorldAccess.Inspection.InfoCardTree.FavorFallback".Translate()
                        : faction.def.royalFavorLabel;

                    var capturedFaction = faction;
                    var capturedPawn = pawn;
                    var capturedTabNode = tabNode;
                    var capturedDialog = dialog;

                    var returnNode = InspectNodeFactory.ActionRow(tabNode,
                        "ReturnAllPermits".Translate() + $" ({returnCost} {favorLabel})", null, () =>
                    {
                        if (!capturedPawn.royalty.PermitsFromFaction(capturedFaction).Any())
                        {
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            TolkHelper.Speak(
                                "NoPermitsToReturn".Loc(capturedPawn.Named("PAWN")),
                                SpeechPriority.High);
                            return;
                        }

                        int cost = InfoCardDataExtractor.TotalReturnPermitsCost(capturedPawn);
                        int currentFavor = capturedPawn.royalty.GetFavor(capturedFaction);
                        if (currentFavor < cost)
                        {
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            TolkHelper.Speak(
                                "NotEnoughFavor".Loc(
                                    cost.Named("FAVORCOST"),
                                    capturedFaction.def.royalFavorLabel.Named("FAVOR"),
                                    capturedPawn.Named("PAWN"),
                                    currentFavor.Named("CURFAVOR")
                                ),
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
                                "RimWorldAccess.Inspection.InfoCardTree.PermitReturnAllResult".Loc(
                                    "ReturnAllPermits".Translate(),
                                    "UnusedPermits".Translate(),
                                    capturedPawn.royalty.GetPermitPoints(capturedFaction)),
                                SpeechPriority.High);
                            RebuildPermitsTab(capturedTabNode, capturedDialog);
                        }, destructive: true));
                    });
                    returnNode.Description = factionSectionName;
                }

                foreach (var (permitName, _, status, description, requiredTitle, def) in group)
                {
                    string label = $"{permitName} - {status}";
                    bool isUnlocked = InfoCardDataExtractor.IsPermitUnlocked(def, pawn, faction);
                    bool isAvailable = def.AvailableForPawn(pawn, faction) && !isUnlocked;

                    bool hasDetails = !string.IsNullOrEmpty(description) || def.minTitle != null ||
                                      def.prerequisite != null || def.cooldownDays > 0 || isAvailable;

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

                            if (capturedDef.minTitle != null)
                            {
                                var curTitle = capturedPawn.royalty.GetCurrentTitle(capturedFaction);
                                bool titleMet = curTitle != null && curTitle.seniority >= capturedDef.minTitle.seniority;
                                string titleStatus = titleMet ? "" : (string)"RimWorldAccess.Inspection.InfoCardTree.NotMetSuffix".Translate();
                                AddChild(permitNode, CreateInfoItem(
                                    "RequiresTitle".Translate(capturedDef.minTitle.GetLabelForBothGenders()).Resolve() + titleStatus,
                                    permitNode.IndentLevel + 1));
                            }

                            if (capturedDef.prerequisite != null)
                            {
                                bool prereqMet = InfoCardDataExtractor.IsPermitUnlocked(
                                    capturedDef.prerequisite, capturedPawn, capturedFaction);
                                string prereqStatus = prereqMet ? "" : (string)"RimWorldAccess.Inspection.InfoCardTree.NotMetSuffix".Translate();
                                AddChild(permitNode, CreateInfoItem(
                                    "UpgradeFrom".Translate(capturedDef.prerequisite.LabelCap).Resolve() + prereqStatus,
                                    permitNode.IndentLevel + 1));
                            }

                            if (capturedDef.cooldownDays > 0)
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    "Cooldown".Translate() + ": " + "PeriodDays".Translate(capturedDef.cooldownDays),
                                    permitNode.IndentLevel + 1));
                            }

                            if (capturedDef.royalAid != null && capturedDef.royalAid.favorCost > 0 &&
                                !capturedFaction.def.royalFavorLabel.NullOrEmpty())
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    "CooldownUseFavorCost".Translate(
                                        capturedFaction.def.royalFavorLabel.Named("HONOR")
                                    ).CapitalizeFirst().Resolve() + ": " + capturedDef.royalAid.favorCost,
                                    permitNode.IndentLevel + 1));
                            }

                            if (!string.IsNullOrEmpty(capturedDescription))
                            {
                                AddChild(permitNode, CreateInfoItem(
                                    capturedDescription.StripTags(), permitNode.IndentLevel + 1));
                            }

                            bool currentlyAvailable = capturedDef.AvailableForPawn(capturedPawn, capturedFaction)
                                && !InfoCardDataExtractor.IsPermitUnlocked(capturedDef, capturedPawn, capturedFaction);

                            if (currentlyAvailable)
                            {
                                var acceptNode = InspectNodeFactory.ActionRow(permitNode, "AcceptPermit".Translate(), null, () =>
                                {
                                    if (!capturedDef.AvailableForPawn(capturedPawn, capturedFaction))
                                    {
                                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                                        TolkHelper.Speak("RimWorldAccess.Inspection.InfoCardTree.PermitNoLongerAvailable".Loc(), SpeechPriority.High);
                                        return;
                                    }

                                    capturedPawn.royalty.AddPermit(capturedDef, capturedFaction);
                                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();

                                    int remainingPoints = capturedPawn.royalty.GetPermitPoints(capturedFaction);
                                    TolkHelper.Speak(
                                        "RimWorldAccess.Inspection.InfoCardTree.PermitGranted".Loc(
                                            capturedDef.LabelCap,
                                            "UnusedPermits".Translate(),
                                            remainingPoints),
                                        SpeechPriority.High);

                                    RebuildPermitsTab(capturedTabNode, capturedDialog);
                                });
                                // ActionRow does not propagate the section name like AddChild.
                                acceptNode.Description = permitNode.Description;
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

        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            InspectNodeFactory.Attach(parent, child);
            // Children inherit the parent's section, so drilling in and back does not
            // re-announce it.
            if (string.IsNullOrEmpty(child.Description) && !string.IsNullOrEmpty(parent.Description))
                child.Description = parent.Description;
        }

        /// <summary>
        /// True in contexts where opening an additional dialog would conflict.
        /// </summary>
        private static bool IsInModalContext()
        {
            if (CaravanFormationState.IsActive) return true;
            if (SplitCaravanState.IsActive) return true;
            if (TradeNavigationState.IsActive) return true;
            if (TransportPodLoadingState.IsActive) return true;
            if (LordJobDialogState.IsActive) return true;

            return false;
        }

        #endregion

        #region Actions Tab

        /// <summary>Builds children for the Actions tab.</summary>
        private static void BuildActionsTabChildren(InspectionTreeItem tabNode, Pawn pawn)
        {
            if (tabNode.Children.Count > 0) return; // Already built

            InspectNodeFactory.ActionRow(tabNode, "Rename".Translate(), pawn, () =>
            {
                InfoCardState.CloseInfoCard();

                // Open Dialog_NamePawn - DialogInterceptionPatch will make it accessible
                Find.WindowStack.Add(pawn.NamePawnDialog());
            });

            // Close the info card before the banish confirmation opens so focus moves to it.
            PawnCommandActionHelper.AddPawnCommandActions(tabNode, pawn, InfoCardState.CloseInfoCard);
        }

        #endregion
    }
}
