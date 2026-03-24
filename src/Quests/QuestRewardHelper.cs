using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public enum RewardPrefType
    {
        RoyalFavor,
        Goodwill
    }

    public class RewardPrefItem
    {
        public Faction Faction;
        public RewardPrefType Type;
        public string Label;
    }

    public class DetailLine
    {
        public string Text;
        public Thing InfoCardThing;
        public Faction InfoCardFaction;

        public DetailLine(string text, Thing thing = null, Faction faction = null)
        {
            Text = text;
            InfoCardThing = thing;
            InfoCardFaction = faction;
        }
    }

    public static class QuestRewardHelper
    {
        /// <summary>
        /// Returns the first QuestPart_Choice from the quest, or null if none exists.
        /// </summary>
        public static QuestPart_Choice GetChoicePart(Quest quest)
        {
            if (quest == null) return null;

            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_Choice choice)
                    return choice;
            }
            return null;
        }

        /// <summary>
        /// Returns true if the quest has a QuestPart_Choice with 2+ choices (requires choosing before accept).
        /// </summary>
        public static bool HasMultipleChoices(Quest quest)
        {
            QuestPart_Choice choicePart = GetChoicePart(quest);
            return choicePart != null && choicePart.choices.Count >= 2;
        }

        /// <summary>
        /// Builds individual reward detail lines with info card targets for the detail view.
        /// Each reward item gets its own line so users can inspect them with Alt+I.
        /// </summary>
        public static List<DetailLine> BuildRewardDetailLines(Quest quest)
        {
            var lines = new List<DetailLine>();
            QuestPart_Choice choicePart = GetChoicePart(quest);

            if (choicePart == null || choicePart.choices.Count == 0)
            {
                lines.Add(new DetailLine("No rewards"));
                return lines;
            }

            if (choicePart.choices.Count == 1)
            {
                // Single reward set
                lines.Add(new DetailLine("Rewards:"));
                AddRewardItemLines(lines, choicePart.choices[0].rewards, "  ");
            }
            else
            {
                // Multiple reward choices
                lines.Add(new DetailLine("Reward choices (choose one to accept):"));
                for (int i = 0; i < choicePart.choices.Count; i++)
                {
                    lines.Add(new DetailLine($"Choice {i + 1}:"));
                    AddRewardItemLines(lines, choicePart.choices[i].rewards, "  ");
                }
            }

            return lines;
        }

        /// <summary>
        /// Adds individual item lines for a set of rewards, with info card targets.
        /// </summary>
        private static void AddRewardItemLines(List<DetailLine> lines, List<Reward> rewards, string indent)
        {
            float totalValue = 0f;

            foreach (Reward reward in rewards)
            {
                totalValue += reward.TotalMarketValue;

                if (reward is Reward_Items rewardItems)
                {
                    if (rewardItems.ItemsListForReading != null && rewardItems.ItemsListForReading.Count > 0)
                    {
                        // Consolidate stacks of the same item into one line
                        var grouped = new Dictionary<string, (int totalCount, Thing representative)>();
                        foreach (Thing item in rewardItems.ItemsListForReading)
                        {
                            if (item == null) continue;
                            string key = item.LabelNoCount;
                            if (grouped.ContainsKey(key))
                            {
                                var existing = grouped[key];
                                grouped[key] = (existing.totalCount + item.stackCount, existing.representative);
                            }
                            else
                            {
                                grouped[key] = (item.stackCount, item);
                            }
                        }

                        foreach (var kvp in grouped)
                        {
                            int count = kvp.Value.totalCount;
                            Thing rep = kvp.Value.representative;
                            string itemName = rep.LabelNoCount.CapitalizeFirst();
                            string label = count > 1
                                ? $"{indent}{count}x {itemName}"
                                : $"{indent}{itemName}";
                            lines.Add(new DetailLine(label, thing: rep));
                        }
                    }
                    else
                    {
                        lines.Add(new DetailLine($"{indent}{DescribeReward(reward)}"));
                    }
                }
                else if (reward is Reward_Goodwill rewardGoodwill)
                {
                    string text = $"{indent}{rewardGoodwill.amount.ToStringWithSign()} goodwill with {rewardGoodwill.faction.Name}";
                    lines.Add(new DetailLine(text, faction: rewardGoodwill.faction));
                }
                else if (reward is Reward_RoyalFavor rewardFavor)
                {
                    string favorLabel = rewardFavor.faction.def.royalFavorLabel.CapitalizeFirst();
                    string text = $"{indent}{rewardFavor.amount.ToStringWithSign()} {favorLabel} with {rewardFavor.faction.Name}";
                    lines.Add(new DetailLine(text, faction: rewardFavor.faction));
                }
                else if (reward is Reward_Pawn rewardPawn)
                {
                    if (rewardPawn.detailsHidden)
                    {
                        lines.Add(new DetailLine($"{indent}Pawn (details hidden)"));
                    }
                    else if (rewardPawn.pawn != null)
                    {
                        lines.Add(new DetailLine($"{indent}Pawn: {rewardPawn.pawn.LabelShort}", thing: rewardPawn.pawn));
                    }
                    else
                    {
                        lines.Add(new DetailLine($"{indent}Pawn"));
                    }
                }
                else if (reward is Reward_DefinedThingDef rewardDef)
                {
                    string label = rewardDef.thingDef != null
                        ? rewardDef.thingDef.LabelCap.ToString()
                        : DescribeReward(reward);
                    lines.Add(new DetailLine($"{indent}{label}"));
                }
                else
                {
                    // Fallback for other reward types - use GetDescription for human-readable text
                    lines.Add(new DetailLine($"{indent}{DescribeReward(reward)}"));
                }
            }

            if (totalValue > 0f)
            {
                lines.Add(new DetailLine($"{indent}Total value: {totalValue.ToStringMoney("F0")}"));
            }
        }

        /// <summary>
        /// Builds a compact reward description string for button labels.
        /// </summary>
        public static string BuildRewardDescription(List<Reward> rewards)
        {
            var parts = new List<string>();

            foreach (Reward reward in rewards)
            {
                parts.Add(DescribeReward(reward));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Type-switched text extraction for a single reward.
        /// </summary>
        public static string DescribeReward(Reward reward)
        {
            if (reward is Reward_Items rewardItems)
            {
                var itemDescs = new List<string>();
                if (rewardItems.ItemsListForReading != null && rewardItems.ItemsListForReading.Count > 0)
                {
                    // Consolidate stacks of the same item
                    var grouped = new Dictionary<string, (int totalCount, Thing representative)>();
                    foreach (Thing item in rewardItems.ItemsListForReading)
                    {
                        if (item == null) continue;
                        string key = item.LabelNoCount;
                        if (grouped.ContainsKey(key))
                        {
                            var existing = grouped[key];
                            grouped[key] = (existing.totalCount + item.stackCount, existing.representative);
                        }
                        else
                        {
                            grouped[key] = (item.stackCount, item);
                        }
                    }

                    foreach (var kvp in grouped)
                    {
                        int count = kvp.Value.totalCount;
                        string itemName = kvp.Value.representative.LabelNoCount.CapitalizeFirst();
                        itemDescs.Add(count > 1 ? $"{count}x {itemName}" : itemName);
                    }
                }
                if (itemDescs.Count == 0)
                {
                    itemDescs.Add("Items");
                }
                return string.Join(", ", itemDescs);
            }
            else if (reward is Reward_Goodwill rg)
            {
                return $"{rg.amount.ToStringWithSign()} goodwill with {rg.faction.Name}";
            }
            else if (reward is Reward_RoyalFavor rf)
            {
                string favorLabel = rf.faction.def.royalFavorLabel.CapitalizeFirst();
                return $"{rf.amount.ToStringWithSign()} {favorLabel} with {rf.faction.Name}";
            }
            else if (reward is Reward_Pawn rp)
            {
                if (rp.detailsHidden) return "Pawn (details hidden)";
                return rp.pawn != null ? $"Pawn: {rp.pawn.LabelShort}" : "Pawn";
            }
            else if (reward is Reward_DefinedThingDef rdt)
            {
                return rdt.thingDef != null ? rdt.thingDef.LabelCap.ToString() : "Item";
            }
            else
            {
                // Fallback: try GetDescription for human-readable text
                try
                {
                    string desc = reward.GetDescription(default(RewardsGeneratorParams));
                    if (!string.IsNullOrEmpty(desc))
                        return desc;
                }
                catch { }
                return reward.GetType().Name.Replace("Reward_", "");
            }
        }

        /// <summary>
        /// Returns the list of reward preference items from all visible factions.
        /// </summary>
        public static List<RewardPrefItem> GetRewardPreferenceItems()
        {
            var items = new List<RewardPrefItem>();

            foreach (Faction faction in Find.FactionManager.AllFactionsVisibleInViewOrder)
            {
                if (faction.IsPlayer)
                    continue;

                if (faction.def.HasRoyalTitles)
                {
                    string status = faction.allowRoyalFavorRewards ? "checked" : "unchecked";
                    string favorLabel = faction.def.royalFavorLabel.CapitalizeFirst();
                    items.Add(new RewardPrefItem
                    {
                        Faction = faction,
                        Type = RewardPrefType.RoyalFavor,
                        Label = $"{faction.Name}: Accept {favorLabel}, {status}"
                    });
                }

                if (faction.CanEverGiveGoodwillRewards)
                {
                    string status = faction.allowGoodwillRewards ? "checked" : "unchecked";
                    string relation = $"currently {faction.PlayerGoodwill.ToStringWithSign()}, {faction.PlayerRelationKind.GetLabelCap()}";
                    items.Add(new RewardPrefItem
                    {
                        Faction = faction,
                        Type = RewardPrefType.Goodwill,
                        Label = $"{faction.Name}: Accept Goodwill, {status}, {relation}"
                    });
                }
            }

            return items;
        }

        /// <summary>
        /// Returns a compact one-line reward summary for a quest.
        /// Used by the list view announcement to describe rewards without detail lines.
        /// </summary>
        public static string BuildCompactRewardSummary(Quest quest)
        {
            QuestPart_Choice choicePart = GetChoicePart(quest);
            if (choicePart == null || choicePart.choices.Count == 0)
                return "No rewards";
            if (choicePart.choices.Count >= 2)
                return "Multiple reward choices available";
            return BuildRewardDescription(choicePart.choices[0].rewards);
        }

        /// <summary>
        /// Toggles the specified reward preference on the faction.
        /// </summary>
        public static void ToggleRewardPreference(RewardPrefItem item)
        {
            if (item == null || item.Faction == null) return;

            switch (item.Type)
            {
                case RewardPrefType.RoyalFavor:
                    item.Faction.allowRoyalFavorRewards = !item.Faction.allowRoyalFavorRewards;
                    break;
                case RewardPrefType.Goodwill:
                    item.Faction.allowGoodwillRewards = !item.Faction.allowGoodwillRewards;
                    break;
            }
        }
    }
}
