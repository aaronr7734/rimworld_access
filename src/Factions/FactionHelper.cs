using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared utility methods for building faction data used by both
    /// FactionLandingState (pre-game) and FactionTabState (in-game).
    /// </summary>
    public static class FactionHelper
    {
        /// <summary>
        /// Builds the list of visible non-player, non-hidden factions
        /// in RimWorld's standard view order (defeated ascending, then listOrderPriority descending).
        /// </summary>
        public static List<Faction> BuildFactionList()
        {
            var result = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if (!faction.IsPlayer && !faction.Hidden)
                {
                    result.Add(faction);
                }
            }
            return result;
        }

        /// <summary>
        /// Builds the full announcement string for a faction including:
        /// name, defeated status, type, leader, relation+goodwill+natural goodwill,
        /// ongoing goodwill events, recent goodwill events,
        /// ideology (if DLC active), and enemy-of list.
        /// </summary>
        public static string BuildFactionAnnouncement(Faction faction)
        {
            var sb = new StringBuilder();

            // Faction name
            sb.Append(faction.Name.CapitalizeFirst());

            // Defeated status
            if (faction.defeated)
                sb.Append(", defeated");

            // Faction type
            AppendSentence(sb, faction.def.LabelCap.Resolve());

            // Leader info
            if (faction.leader != null)
            {
                string leaderTitle = faction.LeaderTitle.CapitalizeFirst();
                string leaderName = faction.leader.Name.ToStringFull;
                AppendSentence(sb, $"{leaderTitle}: {leaderName}");
            }

            // Relation and goodwill
            string relation = faction.PlayerRelationKind.GetLabelCap();
            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                AppendSentence(sb, $"{relation}, goodwill {faction.PlayerGoodwill.ToStringWithSign()}, natural goodwill {faction.NaturalGoodwill.ToStringWithSign()}");

                // Ongoing goodwill events (situations limiting max goodwill)
                string ongoing = BuildOngoingEvents(faction);
                if (!string.IsNullOrEmpty(ongoing))
                    AppendSentence(sb, ongoing);

                // Recent goodwill events (history)
                string recent = BuildRecentEvents(faction);
                if (!string.IsNullOrEmpty(recent))
                    AppendSentence(sb, recent);
            }
            else if (faction.def.permanentEnemy)
            {
                AppendSentence(sb, $"{relation}, permanent enemy");
            }
            else
            {
                AppendSentence(sb, relation);
            }

            // Ideology (if Ideology DLC active and not classic mode)
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
            {
                if (faction.ideos.PrimaryIdeo != null)
                {
                    AppendSentence(sb, $"Ideology: {faction.ideos.PrimaryIdeo.name}");
                }

                var minor = faction.ideos.IdeosMinorListForReading;
                if (minor != null && minor.Count > 0)
                {
                    var minorNames = minor.Select(i => i.name);
                    AppendSentence(sb, $"Minor ideologies: {string.Join(", ", minorNames)}");
                }
            }

            // Enemy-of list
            var enemies = Find.FactionManager.AllFactionsInViewOrder
                .Where(f => f != faction && f.HostileTo(faction) && !f.IsPlayer && !f.Hidden)
                .ToArray();

            if (enemies.Length > 0)
            {
                var enemyNames = enemies.Select(f => f.Name).ToArray();
                AppendSentence(sb, $"Enemy of: {string.Join(", ", enemyNames)}");
            }

            // Faction description (shown as tooltip on hover in vanilla)
            string description = faction.def.Description;
            if (!string.IsNullOrEmpty(description))
                AppendSentence(sb, description);

            return sb.ToString();
        }

        /// <summary>
        /// Appends text as a new sentence, ensuring no double periods.
        /// Adds ". " separator only if the current text doesn't already end with punctuation.
        /// </summary>
        public static void AppendSentence(StringBuilder sb, string text)
        {
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

        /// <summary>
        /// Builds ongoing goodwill events string from GoodwillSituationManager.
        /// Only includes situations that cap max goodwill below 100.
        /// Returns null if no ongoing events.
        /// </summary>
        internal static string BuildOngoingEvents(Faction faction)
        {
            var situations = Find.GoodwillSituationManager.GetSituations(faction);
            var parts = new List<string>();

            for (int i = 0; i < situations.Count; i++)
            {
                if (situations[i].maxGoodwill < 100)
                {
                    string label = situations[i].def.Worker.GetPostProcessedLabelCap(faction);
                    parts.Add($"{label} {situations[i].maxGoodwill.ToStringWithSign()} max");
                }
            }

            if (parts.Count == 0)
                return null;

            return $"Ongoing: {string.Join(", ", parts)}";
        }

        /// <summary>
        /// Builds recent goodwill events string from HistoryEventsManager.
        /// Looks at events within the last 3,600,000 ticks (~60 in-game days).
        /// Returns null if no recent events with goodwill impact.
        /// </summary>
        internal static string BuildRecentEvents(Faction faction)
        {
            var allEventDefs = DefDatabase<HistoryEventDef>.AllDefsListForReading;
            var tmpTicks = new List<int>();
            var tmpCustomGoodwill = new List<int>();
            var parts = new List<string>();

            for (int i = 0; i < allEventDefs.Count; i++)
            {
                int recentCount = Find.HistoryEventsManager.GetRecentCountWithinTicks(
                    allEventDefs[i], 3600000, faction);

                if (recentCount <= 0)
                    continue;

                tmpTicks.Clear();
                tmpCustomGoodwill.Clear();
                Find.HistoryEventsManager.GetRecent(
                    allEventDefs[i], 3600000, tmpTicks, tmpCustomGoodwill, faction);

                int totalGoodwill = 0;
                for (int j = 0; j < tmpCustomGoodwill.Count; j++)
                {
                    totalGoodwill += tmpCustomGoodwill[j];
                }

                if (totalGoodwill != 0)
                {
                    string entry = allEventDefs[i].LabelCap.ToString();
                    if (recentCount != 1)
                        entry += $" x{recentCount}";
                    entry += $" {totalGoodwill.ToStringWithSign()}";
                    parts.Add(entry);
                }
            }

            if (parts.Count == 0)
                return null;

            return $"Recent: {string.Join(", ", parts)}";
        }

        /// <summary>
        /// Builds a treeview of factions. Root is hidden (IndentLevel -1).
        /// Each faction is a collapsible level-0 node with the full announcement as its label.
        /// Children are individual sections (type, leader, relation, etc.).
        /// Description child is expandable with each line as a grandchild.
        /// </summary>
        public static InspectionTreeItem BuildFactionTree(List<Faction> factions)
        {
            var root = new InspectionTreeItem
            {
                Label = "Factions",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true
            };

            foreach (var faction in factions)
            {
                var factionNode = BuildFactionNode(faction);
                factionNode.Parent = root;
                root.Children.Add(factionNode);
            }

            return root;
        }

        private static InspectionTreeItem BuildFactionNode(Faction faction)
        {
            var node = new InspectionTreeItem
            {
                Label = BuildFactionAnnouncement(faction),
                ExpandedLabel = faction.Name,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Data = faction,
                Type = InspectionTreeItem.ItemType.Category
            };

            // Faction type
            AddChildNode(node, faction.def.LabelCap.Resolve());

            // Defeated status
            if (faction.defeated)
                AddChildNode(node, "Defeated");

            // Leader info
            if (faction.leader != null)
            {
                string leaderTitle = faction.LeaderTitle.CapitalizeFirst();
                string leaderName = faction.leader.Name.ToStringFull;
                AddChildNode(node, $"{leaderTitle}: {leaderName}");
            }

            // Relation and goodwill
            string relation = faction.PlayerRelationKind.GetLabelCap();
            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                AddChildNode(node, $"{relation}, goodwill {faction.PlayerGoodwill.ToStringWithSign()}, natural goodwill {faction.NaturalGoodwill.ToStringWithSign()}");

                string ongoing = BuildOngoingEvents(faction);
                if (!string.IsNullOrEmpty(ongoing))
                    AddChildNode(node, ongoing);

                string recent = BuildRecentEvents(faction);
                if (!string.IsNullOrEmpty(recent))
                    AddChildNode(node, recent);
            }
            else if (faction.def.permanentEnemy)
            {
                AddChildNode(node, $"{relation}, permanent enemy");
            }
            else
            {
                AddChildNode(node, relation);
            }

            // Ideology
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
            {
                if (faction.ideos.PrimaryIdeo != null)
                    AddChildNode(node, $"Ideology: {faction.ideos.PrimaryIdeo.name}");

                var minor = faction.ideos.IdeosMinorListForReading;
                if (minor != null && minor.Count > 0)
                {
                    var minorNames = minor.Select(i => i.name);
                    AddChildNode(node, $"Minor ideologies: {string.Join(", ", minorNames)}");
                }
            }

            // Enemy-of list
            var enemies = Find.FactionManager.AllFactionsInViewOrder
                .Where(f => f != faction && f.HostileTo(faction) && !f.IsPlayer && !f.Hidden)
                .ToArray();
            if (enemies.Length > 0)
            {
                var enemyNames = enemies.Select(f => f.Name).ToArray();
                AddChildNode(node, $"Enemy of: {string.Join(", ", enemyNames)}");
            }

            // Description (expandable with lines as grandchildren)
            string description = faction.def.Description;
            if (!string.IsNullOrEmpty(description))
            {
                string[] lines = description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length <= 1)
                {
                    // Single line — non-expandable leaf with the full text
                    AddChildNode(node, description.Trim());
                }
                else
                {
                    // Multi-line — expandable node with each line as a grandchild
                    var descNode = new InspectionTreeItem
                    {
                        Label = description.Replace("\r", "").Replace("\n", " ").Trim(),
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Parent = node,
                        Type = InspectionTreeItem.ItemType.SubCategory
                    };

                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            AddChildNode(descNode, trimmed);
                    }

                    if (descNode.Children.Count > 0)
                        node.Children.Add(descNode);
                }
            }

            return node;
        }

        private static void AddChildNode(InspectionTreeItem parent, string label)
        {
            parent.Children.Add(new InspectionTreeItem
            {
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                IsExpanded = false,
                Parent = parent,
                Type = InspectionTreeItem.ItemType.DetailText
            });
        }
    }
}
