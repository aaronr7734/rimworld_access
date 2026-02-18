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
        private static string BuildOngoingEvents(Faction faction)
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
        private static string BuildRecentEvents(Faction faction)
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
    }
}
