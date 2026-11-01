using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the pawn Health tab ("Health" category). Builds the health
    /// tree: operations/medical-settings actions, pain and bleeding lines, body
    /// part hediff nodes, and the capacities subcategory.
    /// </summary>
    internal sealed class PawnHealthAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Health";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildHealthChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for Health category.
        /// </summary>
        private static void BuildHealthChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Operations action (Full mode only)
            if (mode == InspectionMode.Full)
            {
                // Use mech-specific label for mechanoids
                string operationsLabel = (pawn.RaceProps.IsMechanoid
                    ? "MedicalOperationsMechanoidsShort"
                    : "MedicalOperationsShort").Translate();

                var operationsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = operationsLabel,
                    Data = new InspectSectionDatum(pawn, InspectSectionKind.HealthOperations),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                operationsItem.OnActivate = () =>
                {
                    HealthTabState.OpenOperations(pawn);
                };
                InspectNodeFactory.Attach(parentItem, operationsItem);

                // Medical care settings action (opens the Overview tab). The
                // row label carries the current values the health card shows
                // next to its own controls — gates replicate
                // HealthCardUtility.DrawOverviewTab verbatim.
                var settingValues = new List<string>();
                if (pawn.foodRestriction != null && pawn.foodRestriction.Configurable
                    && !pawn.DevelopmentalStage.Baby() && pawn.needs?.food != null
                    && (!pawn.IsMutant || !pawn.mutant.Def.disablePolicies))
                {
                    settingValues.Add($"{"AllowFood".Translate()}: {pawn.foodRestriction.CurrentFoodPolicy.label}");
                }
                bool playerCare = pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer;
                bool bedCare = pawn.NonHumanlikeOrWildMan() && pawn.InBed() && pawn.CurrentBed().Faction == Faction.OfPlayer;
                if (pawn.RaceProps.IsFlesh && (playerCare || bedCare)
                    && (!pawn.IsMutant || pawn.mutant.Def.entitledToMedicalCare)
                    && pawn.playerSettings != null && !pawn.Dead && Current.ProgramState == ProgramState.Playing)
                {
                    settingValues.Add($"{"AllowMedicine".Translate()}: {MedicalCareUtility.GetLabel(pawn.playerSettings.medCare)}");
                }
                if (Current.ProgramState == ProgramState.Playing && pawn.IsColonist && !pawn.Dead
                    && !pawn.DevelopmentalStage.Baby() && pawn.playerSettings != null)
                {
                    settingValues.Add($"{"AllowSelfTend".Translate()}: {(pawn.playerSettings.selfTend ? "On".Translate() : "Off".Translate())}");
                }
                string settingsLabel = "RimWorldAccess.Inspection.Tree.HealthSettings".Translate();
                if (settingValues.Count > 0)
                {
                    settingsLabel += $": {string.Join(". ", settingValues)}";
                }
                var healthSettingsItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = settingsLabel,
                    Data = new InspectSectionDatum(pawn, InspectSectionKind.HealthSettings),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                healthSettingsItem.OnActivate = () =>
                {
                    HealthTabState.OpenMedicalSettings(pawn);
                };
                InspectNodeFactory.Attach(parentItem, healthSettingsItem);
            }

            // Pain level (flesh pawns only, skip if no pain)
            string painLabel = HealthTabHelper.GetPainLabel(pawn);
            if (painLabel != null)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = painLabel,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Bleeding rate with time-to-death
            string bleedingLabel = HealthTabHelper.GetBleedingLabel(pawn);
            if (bleedingLabel != null)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = bleedingLabel,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Body part nodes — flat list using vanilla's hediff filtering and sort order
            var visibleHediffs = HealthTabHelper.GetVisibleHediffs(pawn).ToList();

            if (visibleHediffs.Count > 0)
            {
                // Group by body part, sorted by vanilla's height/coverage priority
                var hediffsByPart = visibleHediffs
                    .GroupBy(h => h.Part)
                    .OrderByDescending(g => HealthTabHelper.GetHediffListPriority(g.Key));

                foreach (var group in hediffsByPart)
                {
                    var part = group.Key;
                    var partHediffs = group.ToList();
                    string partLabel = part != null ? part.LabelCap.ToString() : "WholeBody".Translate().ToString();

                    var bodyPartItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = partLabel,
                        ExpandedLabel = partLabel,
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = true,
                        IsExpanded = false
                    };
                    // Build children eagerly so collapsed labels include full content immediately
                    BuildBodyPartHediffChildren(bodyPartItem, pawn, part, partHediffs);
                    // Fold the subtree into the collapsed label (matching capacities/hediff
                    // groups) so navigating to — or typeahead-matching — a collapsed body
                    // part speaks its full content. ExpandedLabel stays the short name.
                    var partChildLabels = bodyPartItem.Children.Select(c => c.Label).ToList();
                    if (partChildLabels.Count > 0)
                        bodyPartItem.Label += $": {string.Join(". ", partChildLabels)}";
                    InspectNodeFactory.Attach(parentItem, bodyPartItem);
                }
            }
            else
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"({"NoHealthConditions".Translate()})",
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            // Capacities subcategory — build children eagerly for collapsed summary
            if (pawn.health.capacities != null && !pawn.Dead)
            {
                var capacities = HealthTabHelper.GetCapacities(pawn);
                if (capacities.Count > 0)
                {
                    string capacitiesLabel = "RimWorldAccess.Inspection.Tree.Capacities".Translate();
                    var capacitiesItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Label = capacitiesLabel,
                        ExpandedLabel = capacitiesLabel,
                        Data = new InspectSectionDatum(pawn, InspectSectionKind.HealthCapacities),
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = true,
                        IsExpanded = false
                    };
                    BuildCapacitiesChildren(capacitiesItem, pawn);
                    // Build collapsed summary from children
                    var capChildLabels = capacitiesItem.Children.Select(c => c.Label).ToList();
                    if (capChildLabels.Count > 0)
                        capacitiesItem.Label += $": {string.Join(". ", capChildLabels)}";
                    InspectNodeFactory.Attach(parentItem, capacitiesItem);
                }
            }
        }

        /// <summary>
        /// Builds children for a body part node — groups hediffs by UIGroupKey (like vanilla)
        /// so identical conditions show as "Gunshot wound x3" instead of 3 separate items.
        /// </summary>
        private static void BuildBodyPartHediffChildren(InspectionTreeItem parentItem, Pawn pawn, BodyPartRecord part, List<Hediff> hediffs)
        {
            if (parentItem.Children.Count > 0)
                return;

            // Add body part condition/HP child if damaged
            if (part != null)
            {
                float partHealth = pawn.health.hediffSet.GetPartHealth(part);
                float maxHealth = part.def.GetMaxHealth(pawn);
                if (partHealth < maxHealth * 0.999f)
                {
                    var conditionLabel = HealthUtility.GetPartConditionLabel(pawn, part);
                    string conditionText = $"{conditionLabel.First}, {partHealth} / {maxHealth}";
                    float efficiency = PawnCapacityUtility.CalculatePartEfficiency(pawn.health.hediffSet, part);
                    if (efficiency != 1f)
                        conditionText += $", {"Efficiency".Translate()}: {efficiency.ToStringPercent()}";

                    InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = conditionText,
                        IndentLevel = parentItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                }
            }

            var groups = hediffs.GroupBy(h => h.UIGroupKey).Select(g => g.ToList()).ToList();

            // Single hediff group: skip intermediate node, attach details directly to body part
            if (groups.Count == 1)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = HediffGroupRowLabel(groups[0], pawn),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });

                int childrenBefore = parentItem.Children.Count;
                BuildHediffDetailChildren(parentItem, groups[0], pawn);
                if (parentItem.Children.Count == childrenBefore)
                    parentItem.IsExpandable = false;

                // Children only — the caller folds the collapsed summary into the body part
                // label (see the AddChild site in BuildHealthChildren), matching the capacities
                // pattern. Folding here too would speak the status twice.
                return;
            }

            foreach (var hediffGroup in groups)
            {
                string rowLabel = HediffGroupRowLabel(hediffGroup, pawn);
                var hediffItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = rowLabel,
                    ExpandedLabel = rowLabel,
                    Data = hediffGroup[0],
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpanded = false
                };

                // Build children eagerly for collapsed summary
                BuildHediffDetailChildren(hediffItem, hediffGroup, pawn);
                hediffItem.IsExpandable = hediffItem.Children.Count > 0;
                var hediffChildLabels = hediffItem.Children.Select(c => c.Label).ToList();
                if (hediffChildLabels.Count > 0)
                    hediffItem.Label += $": {string.Join(". ", hediffChildLabels)}";
                InspectNodeFactory.Attach(parentItem, hediffItem);
            }

            // No fold here — the caller folds the collapsed summary from these children into the
            // body part label (matching the capacities pattern). Each hediff child already folds
            // its own detail summary above; folding the parent here too would duplicate the status.
        }

        /// <summary>
        /// Vanilla's row text ("Label xN") for a group; a lone hediff takes its tooltip header
        /// instead, which carries the severity.
        /// </summary>
        private static string HediffGroupRowLabel(List<Hediff> group, Pawn pawn)
        {
            string name = group[0].LabelCap.StripTags();
            if (group.Count > 1)
                return $"{name} x{group.Count}";
            HealthTabHelper.GetHediffTooltipLines(group[0], pawn, out string header);
            return string.IsNullOrEmpty(header) ? name : header;
        }

        /// <summary>
        /// Detail children from vanilla's hover tooltip, which stacks one block per grouped
        /// hediff. Lines repeated across the stack are spoken once.
        /// </summary>
        private static void BuildHediffDetailChildren(InspectionTreeItem hediffItem, List<Hediff> group, Pawn pawn)
        {
            var seen = new HashSet<string>();
            foreach (Hediff hediff in group)
            {
                List<string> lines = HealthTabHelper.GetHediffTooltipLines(hediff, pawn, out string header);
                if (group.Count > 1 && !string.IsNullOrEmpty(header) && header != hediff.LabelCap.StripTags())
                    AttachDetail(hediffItem, header);
                foreach (string line in lines)
                {
                    if (seen.Add(line))
                        AttachDetail(hediffItem, line);
                }
            }

            // Developer info (dev mode + playing only) — mirrors vanilla's
            // showHediffsDebugInfo tooltip block appended after the description.
            string devInfo = HealthTabHelper.GetHediffDeveloperInfo(group[0]);
            if (!string.IsNullOrEmpty(devInfo))
                AttachDetail(hediffItem, devInfo);
        }

        private static void AttachDetail(InspectionTreeItem parent, string label)
        {
            InspectNodeFactory.Attach(parent, new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false
            });
        }

        /// <summary>
        /// Builds children for Capacities subcategory.
        /// Uses vanilla's filtering, sorting, and pawn-type-specific labels.
        /// </summary>
        private static void BuildCapacitiesChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return;

            var capacities = HealthTabHelper.GetCapacities(pawn);

            foreach (var capacity in capacities)
            {
                string capName = capacity.Label;
                bool hasBreakdown = !string.IsNullOrEmpty(capacity.DetailedBreakdown);

                var capacityItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = capName,
                    ExpandedLabel = capName,
                    Data = capacity,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = hasBreakdown,
                    IsExpanded = false
                };

                if (hasBreakdown)
                {
                    // Add level as first child
                    InspectNodeFactory.Attach(capacityItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = capacity.LevelLabel,
                        IndentLevel = capacityItem.IndentLevel + 1,
                        IsExpandable = false
                    });
                    // Build breakdown children eagerly
                    BuildCapacityDetailChildren(capacityItem, capacity);
                    // Build collapsed summary from children
                    var capDetailLabels = capacityItem.Children.Select(c => c.Label).ToList();
                    if (capDetailLabels.Count > 0)
                        capacityItem.Label += $": {string.Join(". ", capDetailLabels)}";
                }
                else
                {
                    // No breakdown — show level inline (non-expandable)
                    capacityItem.Label = $"{capName}: {capacity.LevelLabel}";
                }
                InspectNodeFactory.Attach(parentItem, capacityItem);
            }
        }

        /// <summary>
        /// Builds detail children for a capacity showing impactors.
        /// </summary>
        private static void BuildCapacityDetailChildren(InspectionTreeItem capacityItem, HealthTabHelper.CapacityInfo capacity)
        {
            if (!string.IsNullOrEmpty(capacity.DetailedBreakdown))
            {
                var lines = capacity.DetailedBreakdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        InspectNodeFactory.Attach(capacityItem, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = trimmedLine,
                            IndentLevel = capacityItem.IndentLevel + 1,
                            IsExpandable = false
                        });
                    }
                }
            }
        }
    }
}
