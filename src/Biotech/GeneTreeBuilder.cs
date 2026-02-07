using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds InspectionTreeItem trees from pregnancy GeneSet data.
    /// Used to create accessible navigation for the "Inspect Baby Genes" screen.
    /// </summary>
    public static class GeneTreeBuilder
    {
        /// <summary>
        /// Builds the complete tree for a pregnancy gene set.
        /// </summary>
        /// <param name="geneSet">The baby's gene set from HediffWithParents</param>
        /// <param name="motherName">Optional mother's name for context</param>
        /// <param name="fatherName">Optional father's name for context</param>
        /// <returns>Root tree item with genes as children</returns>
        public static InspectionTreeItem BuildTree(GeneSet geneSet, string motherName = null, string fatherName = null)
        {
            if (geneSet == null)
            {
                return CreateEmptyTree();
            }

            var genes = geneSet.GenesListForReading;
            if (genes == null || genes.Count == 0)
            {
                return CreateEmptyTree();
            }

            // Build root label with xenotype if available
            string rootLabel = BuildRootLabel(geneSet, motherName, fatherName);

            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = rootLabel,
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            // Sort genes by display category priority, then by display order, then alphabetically
            var sortedGenes = genes
                .OrderByDescending(g => g.displayCategory?.displayPriorityInGenepack ?? 0)
                .ThenBy(g => g.displayOrderInCategory)
                .ThenBy(g => g.label)
                .ToList();

            // Add gene nodes
            foreach (var gene in sortedGenes)
            {
                var geneNode = CreateGeneNode(gene, root.IndentLevel + 1);
                AddChild(root, geneNode);
            }

            // Add biostats summary at the end
            AddBiostatsSummary(root, geneSet);

            return root;
        }

        /// <summary>
        /// Builds the root label with gene count and xenotype info.
        /// </summary>
        private static string BuildRootLabel(GeneSet geneSet, string motherName, string fatherName)
        {
            var sb = new StringBuilder();
            sb.Append("Baby Genes");

            string xenotype = geneSet.Label;
            if (!string.IsNullOrEmpty(xenotype) && xenotype != "ERR")
            {
                sb.Append($": {xenotype}");
            }

            int geneCount = geneSet.GenesListForReading?.Count ?? 0;
            sb.Append($" ({geneCount} {(geneCount == 1 ? "gene" : "genes")})");

            return sb.ToString();
        }

        /// <summary>
        /// Creates a tree node for a single gene with expandable details.
        /// </summary>
        private static InspectionTreeItem CreateGeneNode(GeneDef gene, int indent)
        {
            // Build a summary label with key stats
            string label = BuildGeneSummaryLabel(gene);

            var geneNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                Data = gene,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = indent
            };

            // Lazy-load children when expanded
            geneNode.OnActivate = () => BuildGeneDetails(geneNode, gene);

            return geneNode;
        }

        /// <summary>
        /// Builds the summary label for a gene (name and category only, stats shown on expand).
        /// </summary>
        private static string BuildGeneSummaryLabel(GeneDef gene)
        {
            var parts = new List<string>();

            // Try to get a more descriptive label for color genes
            string label = gene.LabelCap;

            // For cosmetic genes, try to add color info if available
            if (IsCosmeticGene(gene))
            {
                string colorDesc = GetColorDescription(gene);
                if (!string.IsNullOrEmpty(colorDesc) && !label.ToLower().Contains(colorDesc.ToLower()))
                {
                    label = $"{label}: {colorDesc}";
                }
            }

            parts.Add(label);

            // Add category if available
            if (gene.displayCategory != null)
            {
                parts.Add($"({gene.displayCategory.LabelCap})");
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Tries to get a color description for cosmetic genes.
        /// </summary>
        private static string GetColorDescription(GeneDef gene)
        {
            // Check for hair color
            if (gene.hairColorOverride.HasValue)
            {
                return DescribeColor(gene.hairColorOverride.Value);
            }

            // Check for skin color - use perceptual luminance for skin-specific shade names
            if (gene.skinColorOverride.HasValue)
            {
                return DescribeSkinShade(gene.skinColorOverride.Value);
            }
            if (gene.skinColorBase.HasValue)
            {
                return DescribeSkinShade(gene.skinColorBase.Value);
            }

            return null;
        }

        /// <summary>
        /// Converts a skin color to a human-readable shade using perceptual luminance.
        /// Matches the labels used in InfoCardDataExtractor for consistency.
        /// </summary>
        private static string DescribeSkinShade(UnityEngine.Color color)
        {
            float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            if (luminance > 0.85f) return "very light";
            if (luminance > 0.7f) return "light";
            if (luminance > 0.55f) return "fair";
            if (luminance > 0.45f) return "medium";
            if (luminance > 0.35f) return "tan";
            if (luminance > 0.2f) return "brown";
            return "dark brown";
        }

        /// <summary>
        /// Converts a Unity Color to a human-readable description for non-skin colors (hair, etc.).
        /// </summary>
        private static string DescribeColor(UnityEngine.Color color)
        {
            float r = color.r;
            float g = color.g;
            float b = color.b;
            float brightness = (r + g + b) / 3f;

            // Check for grayscale
            float maxChannel = Math.Max(r, Math.Max(g, b));
            float minChannel = Math.Min(r, Math.Min(g, b));
            float saturation = maxChannel > 0 ? (maxChannel - minChannel) / maxChannel : 0;

            if (saturation < 0.15f)
            {
                if (brightness < 0.2f) return "very dark";
                if (brightness < 0.35f) return "dark";
                if (brightness < 0.5f) return "medium-dark";
                if (brightness < 0.65f) return "medium";
                if (brightness < 0.8f) return "light";
                return "very light";
            }

            // Colored - find dominant hue
            if (r > g && r > b)
            {
                if (g > b) return brightness > 0.5f ? "orange" : "brown";
                return brightness > 0.5f ? "pink" : "red";
            }
            if (g > r && g > b)
            {
                return brightness > 0.5f ? "light green" : "green";
            }
            if (b > r && b > g)
            {
                return brightness > 0.5f ? "light blue" : "blue";
            }

            // Mixed colors
            if (r > 0.4f && g > 0.4f && b < 0.3f) return "blonde";
            if (r > 0.3f && g > 0.3f && b > 0.3f) return "gray";

            return null;
        }

        /// <summary>
        /// Builds the detail children for a gene node using DescriptionFull.
        /// </summary>
        private static void BuildGeneDetails(InspectionTreeItem geneNode, GeneDef gene)
        {
            if (geneNode.Children.Count > 0)
                return; // Already built

            int childIndent = geneNode.IndentLevel + 1;

            // Use the game's DescriptionFull which contains all tooltip information
            string fullDescription = gene.DescriptionFull;
            if (!string.IsNullOrEmpty(fullDescription))
            {
                // Strip color tags but keep the text structure
                fullDescription = fullDescription.StripTags();

                // Split by double newlines to get sections, then by single newlines for lines
                var sections = fullDescription.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var section in sections)
                {
                    var lines = section.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    if (lines.Length == 1)
                    {
                        // Single line section - add directly
                        string line = lines[0].Trim();
                        if (!string.IsNullOrEmpty(line))
                        {
                            AddChild(geneNode, CreateInfoItem(line, childIndent));
                        }
                    }
                    else if (lines.Length > 1)
                    {
                        // Multi-line section - check if it has a header (ends with :)
                        string firstLine = lines[0].Trim();
                        if (firstLine.EndsWith(":"))
                        {
                            // This is a header with sub-items
                            var sectionNode = new InspectionTreeItem
                            {
                                Type = InspectionTreeItem.ItemType.SubCategory,
                                Label = firstLine.TrimEnd(':'),
                                IsExpandable = true,
                                IsExpanded = false,
                                IndentLevel = childIndent
                            };

                            // Add the sub-items
                            for (int i = 1; i < lines.Length; i++)
                            {
                                string subLine = lines[i].Trim().TrimStart('-', ' ');
                                if (!string.IsNullOrEmpty(subLine))
                                {
                                    AddChild(sectionNode, CreateInfoItem(subLine, childIndent + 1));
                                }
                            }

                            if (sectionNode.Children.Count > 0)
                            {
                                AddChild(geneNode, sectionNode);
                            }
                        }
                        else
                        {
                            // Multi-line without header - join into single item
                            string combined = string.Join(" ", lines.Select(l => l.Trim()));
                            AddChild(geneNode, CreateInfoItem(combined, childIndent));
                        }
                    }
                }
            }

            // If no details were added, provide a basic message
            if (geneNode.Children.Count == 0)
            {
                // For cosmetic genes, note that they have no gameplay effects
                if (IsCosmeticGene(gene))
                {
                    AddChild(geneNode, CreateInfoItem("Cosmetic gene with no gameplay effects", childIndent));
                }
                else
                {
                    AddChild(geneNode, CreateInfoItem("No additional details available", childIndent));
                }
            }
        }

        /// <summary>
        /// Checks if a gene is purely cosmetic (no biostats, no effects).
        /// </summary>
        private static bool IsCosmeticGene(GeneDef gene)
        {
            return gene.biostatCpx == 0 &&
                   gene.biostatMet == 0 &&
                   gene.biostatArc == 0 &&
                   (gene.statOffsets == null || gene.statOffsets.Count == 0) &&
                   (gene.statFactors == null || gene.statFactors.Count == 0) &&
                   (gene.capMods == null || gene.capMods.Count == 0) &&
                   (gene.abilities == null || gene.abilities.Count == 0) &&
                   (gene.forcedTraits == null || gene.forcedTraits.Count == 0);
        }

        /// <summary>
        /// Adds a biostats summary section at the end of the tree.
        /// </summary>
        private static void AddBiostatsSummary(InspectionTreeItem root, GeneSet geneSet)
        {
            int complexity = geneSet.ComplexityTotal;
            int metabolism = geneSet.MetabolismTotal;
            int archites = geneSet.ArchitesTotal;

            // Build inline summary
            var parts = new List<string>();
            parts.Add($"Complexity {complexity}");
            parts.Add($"Metabolism {metabolism.ToStringWithSign()}");
            if (archites > 0)
            {
                parts.Add($"Archites {archites}");
            }

            string summaryLabel = $"Total Biostats: {string.Join(", ", parts)}";

            var summaryNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = summaryLabel,
                IsExpandable = true,
                IsExpanded = false,
                IndentLevel = 0
            };

            // Add expanded details with explanations inline
            summaryNode.OnActivate = () =>
            {
                if (summaryNode.Children.Count > 0) return;

                // Complexity with explanation inline
                string complexityDesc = ((string)"ComplexityDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem($"Complexity: {complexity}. {complexityDesc}", summaryNode.IndentLevel + 1));

                // Metabolism with explanation inline
                string metabolismDesc = ((string)"MetabolismDesc".Translate()).StripTags();
                AddChild(summaryNode, CreateInfoItem($"Metabolism: {metabolism.ToStringWithSign()}. {metabolismDesc}", summaryNode.IndentLevel + 1));

                // Archites if present
                if (archites > 0)
                {
                    string architesDesc = ((string)"ArchitesRequiredDesc".Translate()).StripTags();
                    AddChild(summaryNode, CreateInfoItem($"Archites Required: {archites}. {architesDesc}", summaryNode.IndentLevel + 1));
                }
            };

            AddChild(root, summaryNode);
        }

        /// <summary>
        /// Creates an empty tree for when no genes are available.
        /// </summary>
        private static InspectionTreeItem CreateEmptyTree()
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Baby Genes: None",
                IsExpandable = false,
                IsExpanded = false,
                IndentLevel = -1
            };

            return root;
        }

        /// <summary>
        /// Creates a simple info item (non-expandable detail text).
        /// </summary>
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

        /// <summary>
        /// Adds a child to a parent and sets the parent reference.
        /// </summary>
        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }
    }
}
