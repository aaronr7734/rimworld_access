using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Stateless quest-list/detail content builders. Every method is a pure function of its arguments
    /// — no static field, no mutation, no announcement side effect — so it can be called from tests or
    /// other menus. <see cref="QuestMenuState"/> keeps the session data and every mutation;
    /// <see cref="RimWorldAccess.Shell.QuestMenuScope"/> keeps the cursor/region/typeahead state and
    /// passes it in as plain parameters. <see cref="QuestMenuState.QuestsTab"/> is internal rather
    /// than private so these builders can take it as a parameter.
    /// </summary>
    public static class QuestMenuHelper
    {
        /// <summary>
        /// Filters and sorts the full quest list down to one tab's contents. The caller feeds the count
        /// into its Quests region's <c>ListModel</c>, which clamps the cursor itself; no navigation
        /// state lives here.
        /// </summary>
        internal static List<Quest> BuildQuestList(QuestMenuState.QuestsTab tab, bool showAll)
        {
            List<Quest> result = new List<Quest>();
            List<Quest> allQuests = Find.QuestManager.questsInDisplayOrder;

            foreach (Quest quest in allQuests)
            {
                if (ShouldShowQuest(quest, tab, showAll))
                {
                    result.Add(quest);
                }
            }

            switch (tab)
            {
                case QuestMenuState.QuestsTab.Available:
                    result = result.OrderBy(q => q.TicksUntilExpiry).ToList();
                    break;
                case QuestMenuState.QuestsTab.Active:
                    result = result.OrderBy(q => q.TicksSinceAccepted).ToList();
                    break;
                case QuestMenuState.QuestsTab.Historical:
                    result = result.OrderBy(q => q.TicksSinceCleanup).ToList();
                    break;
            }

            return result;
        }

        internal static bool ShouldShowQuest(Quest quest, QuestMenuState.QuestsTab tab, bool showAll)
        {
            // Mirrors MainTabWindow_Quests.ShouldListNow: hiddenInUI is always filtered, hidden is
            // revealed only under the DEV "Show all" toggle.
            if (quest.hiddenInUI)
                return false;
            if (quest.hidden && !showAll)
                return false;

            switch (tab)
            {
                case QuestMenuState.QuestsTab.Available:
                    return quest.State == QuestState.NotYetAccepted && !quest.dismissed;
                case QuestMenuState.QuestsTab.Active:
                    return quest.State == QuestState.Ongoing && !quest.dismissed;
                case QuestMenuState.QuestsTab.Historical:
                    return quest.Historical || quest.dismissed;
                default:
                    return false;
            }
        }

        internal static QuestMenuState.QuestsTab GetTabForQuest(Quest quest)
        {
            if (quest.Historical || quest.dismissed)
                return QuestMenuState.QuestsTab.Historical;

            if (quest.State == QuestState.NotYetAccepted)
                return QuestMenuState.QuestsTab.Available;

            if (quest.State == QuestState.Ongoing)
                return QuestMenuState.QuestsTab.Active;

            return QuestMenuState.QuestsTab.Historical;
        }

        internal static string GetTabName(QuestMenuState.QuestsTab tab)
        {
            switch (tab)
            {
                case QuestMenuState.QuestsTab.Available:
                    return "RimWorldAccess.Quests.Tab.Available".Translate();
                case QuestMenuState.QuestsTab.Active:
                    return "RimWorldAccess.Quests.Tab.Active".Translate();
                case QuestMenuState.QuestsTab.Historical:
                    return "RimWorldAccess.Quests.Tab.Historical".Translate();
                default:
                    return "RimWorldAccess.Quests.Tab.Generic".Translate();
            }
        }

        public static string GetShortTimeInfo(Quest quest)
        {
            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry >= 0)
            {
                return "RimWorldAccess.Quests.List.ExpiresIn".Translate(
                    quest.TicksUntilExpiry.ToStringTicksToPeriod(allowSeconds: true, shortForm: true)).ToString();
            }
            else if (quest.Historical)
            {
                return "RimWorldAccess.Quests.List.AgoOnly".Translate(
                    quest.TicksSinceCleanup.ToStringTicksToPeriod(allowSeconds: false, shortForm: true));
            }
            else if (quest.EverAccepted)
            {
                // An active quest's bad-outcome deadline takes priority over "accepted ago"
                // (MainTabWindow_Quests.GetShortTimeInfo).
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_Delay delayPart &&
                        delayPart.State == QuestPartState.Enabled &&
                        delayPart.isBad &&
                        !delayPart.expiryInfoPart.NullOrEmpty())
                    {
                        return "QuestExpiresIn".Translate(
                            delayPart.TicksLeft.ToStringTicksToPeriod(allowSeconds: false, shortForm: true, canUseDecimals: false)).ToString();
                    }
                }
                return (string)"RimWorldAccess.Quests.List.AcceptedAgo".Translate(quest.TicksSinceAccepted.ToStringTicksToPeriod(allowSeconds: false, shortForm: true));
            }

            return "";
        }

        /// <summary>
        /// A quest's Quests-region row text: name+status, difficulty and timing. The Label must NOT
        /// embed position — the shared composer fills PositionIndex/Count from the region's cursor and
        /// honors the AnnouncePosition setting, so embedding it here would speak it twice. The
        /// description and the rewards ride the row's Extras in that order, because vanilla's right
        /// pane draws DoDescription before DoRewards (MainTabWindow_Quests.cs:471,474) and this row is
        /// a spoken reading of that pane.
        /// </summary>
        public static string BuildQuestRowLabel(Quest quest)
        {
            var parts = new List<string>();
            string name = quest.name.StripTags();

            if (quest.dismissed && !quest.Historical)
                parts.Add("RimWorldAccess.Quests.List.NameWithStatus".Translate(
                    name, "RimWorldAccess.Quests.Status.Dismissed".Translate()));
            else if (quest.Historical)
            {
                string statusKey;
                switch (quest.State)
                {
                    case QuestState.EndedSuccess:
                        statusKey = "RimWorldAccess.Quests.Status.Completed";
                        break;
                    case QuestState.EndedFailed:
                        statusKey = "RimWorldAccess.Quests.Status.Failed";
                        break;
                    default:
                        statusKey = "RimWorldAccess.Quests.Status.Expired";
                        break;
                }
                parts.Add("RimWorldAccess.Quests.List.NameWithStatus".Translate(name, statusKey.Translate()));
            }
            else
            {
                parts.Add(name);
            }

            int rating = Math.Max(quest.challengeRating, 1);
            string ratingText = rating == 1
                ? "RimWorldAccess.Quests.List.StarsOne".Translate().ToString()
                : "RimWorldAccess.Quests.List.StarsMany".Translate(rating).ToString();
            if (quest.charity)
                ratingText += "RimWorldAccess.Quests.List.CharitySuffix".Translate();
            parts.Add(ratingText);

            string timeInfo = GetShortTimeInfo(quest);
            if (!string.IsNullOrEmpty(timeInfo))
                parts.Add(timeInfo);

            return string.Join(". ", parts);
        }

        /// <summary>The rewards half of a Quests-region row; see <see cref="BuildQuestRowLabel"/> for the Extras order.</summary>
        public static string BuildQuestRowRewards(Quest quest)
        {
            string summary = QuestRewardHelper.BuildCompactRewardSummary(quest);
            return summary.NullOrEmpty()
                ? ""
                : "RimWorldAccess.Quests.List.RewardsSummary".Translate(summary).ToString();
        }

        /// <summary>The Details region's header row text (Detail.HeaderStatus plus the six Status.* keys).</summary>
        public static string BuildDetailHeader(Quest quest)
        {
            string name = quest.name.StripTags();
            string statusKey;

            if (quest.State == QuestState.NotYetAccepted) statusKey = "RimWorldAccess.Quests.Status.Available";
            else if (quest.State == QuestState.Ongoing && !quest.dismissed) statusKey = "RimWorldAccess.Quests.Status.Active";
            else if (quest.State == QuestState.Ongoing && quest.dismissed) statusKey = "RimWorldAccess.Quests.Status.Dismissed";
            else if (quest.State == QuestState.EndedSuccess) statusKey = "RimWorldAccess.Quests.Status.Completed";
            else if (quest.State == QuestState.EndedFailed) statusKey = "RimWorldAccess.Quests.Status.Failed";
            else statusKey = "RimWorldAccess.Quests.Status.Expired";

            return "RimWorldAccess.Quests.Detail.HeaderStatus".Translate(name, statusKey.Translate());
        }

        /// <summary>Builds the navigable content lines for the detail view.</summary>
        /// <param name="descriptionTextProvider">
        /// An optional override for the description text normally read off <c>quest.description</c>.
        /// <see cref="RimWorldAccess.Shell.QuestMenuScope"/> passes
        /// <see cref="RimTalkQuestsCompat.GetDescriptionOrMarker(Quest, Func{bool}, Action)"/>-backed
        /// closures, so a quest whose description a mod is streaming reads as the "still being
        /// written" marker instead of mid-stream text, arming a fire-once re-announcement. Null reads
        /// <c>quest.description</c> directly.
        /// </param>
        public static List<DetailLine> BuildDetailContentLines(Quest quest, Func<string> descriptionTextProvider = null)
        {
            var lines = new List<DetailLine>();

            int rating = Math.Max(quest.challengeRating, 1);
            string ratingLine = rating == 1
                ? "RimWorldAccess.Quests.Detail.DifficultyOne".Translate().ToString()
                : "RimWorldAccess.Quests.Detail.DifficultyMany".Translate(rating).ToString();
            if (quest.charity)
                ratingLine += "RimWorldAccess.Quests.Detail.CharitySuffix".Translate();
            lines.Add(new DetailLine(ratingLine));

            if (quest.State == QuestState.NotYetAccepted && quest.TicksUntilExpiry > 0)
            {
                lines.Add(new DetailLine("RimWorldAccess.Quests.Detail.ExpiresIn".Translate(
                    quest.TicksUntilExpiry.ToStringTicksToPeriod()).ToString()));
            }
            else if (quest.EverAccepted && !quest.Historical)
            {
                lines.Add(new DetailLine("RimWorldAccess.Quests.Detail.AcceptedAgo".Translate(
                    quest.TicksSinceAccepted.ToStringTicksToPeriod())));
            }
            else if (quest.Historical)
            {
                string outcomeKey = quest.State == QuestState.EndedSuccess ? "RimWorldAccess.Quests.Status.Completed" :
                                    quest.State == QuestState.EndedFailed ? "RimWorldAccess.Quests.Status.Failed" :
                                    "RimWorldAccess.Quests.Status.Expired";
                lines.Add(new DetailLine("RimWorldAccess.Quests.Detail.Status".Translate(outcomeKey.Translate())));
                lines.Add(new DetailLine("RimWorldAccess.Quests.Detail.Finished".Translate(
                    quest.TicksSinceCleanup.ToStringTicksToPeriod())));
            }

            // Active-quest deadlines from QuestPartActivable parts (matching
            // MainTabWindow_Quests.DoRightAlignedInfo); each ExpiryInfoPart is already localized.
            if (quest.State == QuestState.Ongoing)
            {
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPartActivable activable &&
                        activable.State == QuestPartState.Enabled &&
                        !activable.ExpiryInfoPart.NullOrEmpty())
                    {
                        lines.Add(new DetailLine(activable.ExpiryInfoPart));
                    }
                }
            }

            if (!quest.description.RawText.NullOrEmpty())
            {
                string desc = (descriptionTextProvider != null ? descriptionTextProvider() : quest.description.Resolve()).StripTags();
                string[] descLines = desc.Split('\n');
                foreach (string line in descLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(new DetailLine(trimmed));
                }
            }

            var rewardLines = QuestRewardHelper.BuildRewardDetailLines(quest);
            lines.AddRange(rewardLines);
            AddHyperlinkLines(lines, quest);

            return lines;
        }

        /// <summary>Mirrors MainTabWindow_Quests.DoDefHyperlinks, hidden-quest skip included.</summary>
        private static void AddHyperlinkLines(List<DetailLine> lines, Quest quest)
        {
            foreach (Dialog_InfoCard.Hyperlink hyperlink in quest.Hyperlinks)
            {
                string text;
                if (hyperlink.quest != null && (hyperlink.quest.IsSubquestOf(quest) || quest.IsSubquestOf(hyperlink.quest)))
                {
                    if (quest.hidden || hyperlink.quest.hidden)
                    {
                        continue;
                    }
                    text = "";
                    if (hyperlink.quest.Historical)
                    {
                        text += "(" + "Finished".Translate().ToLower() + ") ";
                    }
                    text += hyperlink.quest.IsSubquestOf(quest) ? "HasSubquest".Translate() : "SubquestOf".Translate();
                    text = text + ": " + hyperlink.Label;
                }
                else
                {
                    text = "ViewHyperlink".Translate(hyperlink.Label);
                }
                lines.Add(new DetailLine(text.StripTags(), hyperlink: hyperlink));
            }
        }

        /// <summary>
        /// The read-only text half of vanilla's DEV debug-info panel
        /// (MainTabWindow_Quests.DoDebugInfo ~1246-1265): quest id, state, the Scribe save dump and the
        /// enabled QuestPartActivable list, one <see cref="DetailLine"/> per logical line. Appended
        /// while the DEV "Show debug info" toggle is on. The panel's interactive signal-sending
        /// controls are deliberately omitted; field names are vanilla dev-tool literals, verbatim.
        /// </summary>
        public static List<DetailLine> BuildDebugInfoLines(Quest quest)
        {
            var lines = new List<DetailLine>();
            lines.Add(new DetailLine("Id: " + quest.id));
            lines.Add(new DetailLine("State: " + quest.State));

            lines.Add(new DetailLine("Data:"));
            string data = Scribe.saver.DebugOutputFor(quest);
            if (!string.IsNullOrEmpty(data))
            {
                foreach (string line in data.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(new DetailLine(trimmed));
                }
            }

            lines.Add(new DetailLine("Active QuestParts:"));
            bool anyActive = false;
            foreach (QuestPart part in quest.PartsListForReading)
            {
                if (part is QuestPartActivable activable && activable.State == QuestPartState.Enabled)
                {
                    lines.Add(new DetailLine(activable.ToString()));
                    anyActive = true;
                }
            }
            if (!anyActive)
                lines.Add(new DetailLine("None"));

            return lines;
        }
    }
}
