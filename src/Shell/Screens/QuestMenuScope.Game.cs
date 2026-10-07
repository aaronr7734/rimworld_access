using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the quest menu (F7), with three content regions:
    /// <list type="number">
    /// <item>Quests -- one row per quest in the current tab (Available/Active/Historical, cycled by
    /// Left/Right from this region only, see <see cref="InQuestsRegion"/>). The Label carries name,
    /// status, difficulty, timing and every reward on offer; the description rides Extras. An empty
    /// tab still shows one read-only "no quests" row, so region cycling can never skip past
    /// it.</item>
    /// <item>Details -- rows derived LIVE from the quest under the Quests cursor; links are buttons, the rest read-only.</item>
    /// <item>Reward preferences -- checkbox rows built the way <see cref="RewardPrefsScope"/> builds
    /// its own, with Check riding the Checkbox role rather than <see cref="RewardPrefItem.Label"/>'s
    /// baked-in state word, so state is never spoken twice.</item>
    /// </list>
    /// The Buttons region is rebuilt from the current quest every read, never cached. Alt+A/Alt+D
    /// act on that quest from ANY region; Alt+I stays Details-only, including its spoken
    /// no-info-card fallback. The reward-choice float menu and its item-inspection sub-mode belong to
    /// <see cref="QuestRewardOverlayScope"/> and <see cref="FloatMenuOverlayScope"/>;
    /// <see cref="QuestMenuScopeMirror"/> carries the matching pop condition and the per-frame
    /// CleanupRewardMenu external-death check.
    /// </summary>
    public sealed class QuestMenuScope : ScreenScope
    {
        private const int QuestsRegion = 0;
        private const int DetailsRegion = 1;
        private const int RewardPrefsRegion = 2;

        private List<Quest> quests = new List<Quest>();
        private List<DetailLine> detailLines = new List<DetailLine>();
        private List<RewardPrefItem> rewardPrefItems = new List<RewardPrefItem>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public QuestMenuScope()
        {
            // Fires after any mutation that changes which quests are listed.
            QuestMenuState.PostQuestListChangeHook = HandlePostQuestMutation;

            Claim(SharedMenuGrammar.Cancel, OnCancel);
            Claim("quest.previousTab", delegate { SwitchQuestsTab(-1); }, when: InQuestsRegion);
            Claim("quest.nextTab", delegate { SwitchQuestsTab(1); }, when: InQuestsRegion);
            Claim("quest.accept", delegate { DoAccept(); });
            Claim("quest.dismiss", delegate { DoDismiss(); });
            Claim("quest.infoCard", delegate { OpenInfoCard(); }, when: InDetailsRegion);
        }

        public override string Name
        {
            get { return "quest-menu"; }
        }

        /// <summary>Windowless -- no vanilla Escape to defer to, so this scope always owns cancel.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        public override void OnPush()
        {
            base.OnPush();
            // This scope is a singleton reused across open/close sessions.
            ResetOpenAnnouncement();
        }

        /// <summary>
        /// The screen name alone. The base then speaks the focused row itself, so this must not
        /// repeat any row content.
        /// </summary>
        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Quests.Menu.Title".Translate();
        }

        public override void OnFocus()
        {
            base.OnFocus();

            Quest pending = QuestMenuState.PendingSelectQuest;
            QuestMenuState.PendingSelectQuest = null;
            if (pending == null)
            {
                return;
            }
            // Land the cursor before the base's entry announcement fires, so it reads this quest.
            if (!TrySelectQuest(pending))
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Menu.QuestNoLongerAvailable".Loc());
                QuestMenuState.Close();
            }
        }

        /// <summary>
        /// Resolves <paramref name="quest"/> to a Quests-region row: the already-guessed tab first,
        /// then every tab in enum order, since the quest may have changed state since the request.
        /// </summary>
        private bool TrySelectQuest(Quest quest)
        {
            RefreshModel();
            QuestMenuState.QuestsTab originalTab = QuestMenuState.CurrentTab;
            int index = quests.FindIndex(q => q == quest);
            if (index < 0)
            {
                foreach (QuestMenuState.QuestsTab tab in Enum.GetValues(typeof(QuestMenuState.QuestsTab)))
                {
                    QuestMenuState.CurrentTab = tab;
                    RefreshModel();
                    index = quests.FindIndex(q => q == quest);
                    if (index >= 0)
                    {
                        break;
                    }
                }
            }
            if (index < 0)
            {
                QuestMenuState.CurrentTab = originalTab;
                RefreshModel();
                return false;
            }
            Model.Region(QuestsRegion).MoveTo(index);
            return true;
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // The base ctor's typeahead-clear Cancel claim, registered first, wins Escape whenever a
            // search is active, so this handler only runs with no active search.
            QuestMenuState.Close();
        }

        /// <summary>
        /// The tab strip belongs to the quest list alone, so Left/Right cycle tabs from the Quests
        /// region only. An empty tab keeps a "no quests" placeholder row, so the region reads as a
        /// named row rather than the shell's bare "empty" announcement.
        /// </summary>
        private bool InQuestsRegion()
        {
            RefreshModel();
            return Model.RegionIndex == QuestsRegion;
        }

        private bool InDetailsRegion()
        {
            RefreshModel();
            return Model.RegionIndex == DetailsRegion;
        }

        /// <summary>Cycles the quest tab, resets the cursor to the first row, and speaks the tab-count summary then that row.</summary>
        private void SwitchQuestsTab(int direction)
        {
            QuestMenuState.AdvanceTab(direction);
            RefreshModel();
            Model.Region(QuestsRegion).MoveFirst();
            TypeaheadReset();

            string tabName = QuestMenuHelper.GetTabName(QuestMenuState.CurrentTab);
            string summary = quests.Count == 0
                ? "RimWorldAccess.Quests.Tab.NoQuests".Translate(tabName).ToString()
                : quests.Count == 1
                    ? "RimWorldAccess.Quests.Tab.WithCountOne".Translate(tabName).ToString()
                    : "RimWorldAccess.Quests.Tab.WithCountMany".Translate(tabName, quests.Count).ToString();
            TolkHelper.SpeakData(summary);

            if (quests.Count > 0)
            {
                AnnounceCurrent(CellAxis.Row);
            }
        }

        private Quest CurrentQuest()
        {
            int index = 0;
            if (Model.RegionCount > QuestsRegion)
            {
                ListModel region = Model.Region(QuestsRegion);
                if (region.IsEmpty)
                {
                    return null;
                }
                index = region.Index;
            }
            if (index < 0 || index >= quests.Count)
            {
                return null;
            }
            return quests[index];
        }

        /// <summary>The quest under the keyboard cursor, or null when the Quests region is empty. The patch classes below Select(), highlight and scroll to it on the real vanilla window.</summary>
        internal Quest FocusedQuest
        {
            get { return CurrentQuest(); }
        }

        /// <summary>
        /// Settle-aware description provider: reads the "still being written" marker while
        /// <see cref="RimTalkQuestsCompat.IsQuestDescriptionGenerating"/> holds, then arms a
        /// fire-once re-announcement once it settles, provided the cursor still rests on this quest.
        /// The overlay checks mirror <see cref="QuestMenuScopeMirror"/>'s push condition exactly:
        /// <c>QuestMenuState.IsActive</c> stays true while this scope is popped under an overlay, and
        /// re-announcing then would speak over whichever screen is actually focused.
        /// </summary>
        private Func<string> DescriptionProvider(Quest quest)
        {
            return () => RimTalkQuestsCompat.GetDescriptionOrMarker(
                quest,
                () => QuestMenuState.IsActive && !InfoCardState.IsActive && !WindowlessFloatMenuState.IsActive
                    && CurrentQuest() == quest
                    && (Model.RegionIndex == DetailsRegion || Model.RegionIndex == QuestsRegion),
                AnnounceCurrentItem);
        }

        private void DoAccept()
        {
            RefreshModel();
            Quest quest = CurrentQuest();
            if (quest == null)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.CannotAccept".Loc(), SpeechPriority.High);
                return;
            }
            QuestMenuState.AcceptQuest(quest);
        }

        private void DoDismiss()
        {
            RefreshModel();
            Quest quest = CurrentQuest();
            if (quest == null)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Menu.NoQuestSelected".Loc());
                return;
            }
            QuestMenuState.ToggleDismissQuest(quest);
        }

        private void OpenInfoCard()
        {
            RefreshModel();
            if (Model.RegionCount <= DetailsRegion)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            ListModel region = Model.Region(DetailsRegion);
            if (region.IsEmpty)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            int lineIndex = region.Index - 1; // row 0 is the header
            if (lineIndex < 0 || lineIndex >= detailLines.Count)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            DetailLine line = detailLines[lineIndex];
            if (line.Hyperlink.HasValue && !line.Hyperlink.Value.IsHidden)
            {
                ActivateHyperlink(line.Hyperlink.Value);
                return;
            }
            if (line.InfoCardThing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardThing));
                return;
            }
            if (line.InfoCardFaction != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(line.InfoCardFaction));
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        /// <summary>Quest links move this session's cursor: <see cref="QuestWindowSelectionPatch"/> re-selects the focused quest every pass, undoing vanilla's Select.</summary>
        private void ActivateHyperlink(Dialog_InfoCard.Hyperlink hyperlink)
        {
            if (hyperlink.quest == null)
            {
                hyperlink.ActivateHyperlink();
                return;
            }
            if (!TrySelectQuest(hyperlink.quest))
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Menu.QuestNoLongerAvailable".Loc());
                return;
            }
            MoveResult result = Model.MoveToRegion(QuestsRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            AnnounceRegion();
        }

        /// <summary>
        /// Fired by QuestMenuState after an accept/dismiss/dev-instant-accept mutation. From
        /// anywhere but the Quests region it returns focus there and announces the region; from the
        /// Quests region it re-reads the current row, whose position may have shifted. "Anywhere
        /// else" must include the Buttons region: accepting deletes both the Details and Buttons
        /// regions, so the region index would clamp down onto the faction checkboxes.
        /// </summary>
        private void HandlePostQuestMutation()
        {
            bool wasElsewhere = Model.RegionIndex != QuestsRegion;
            RefreshModel();
            if (wasElsewhere)
            {
                MoveResult result = Model.MoveToRegion(QuestsRegion);
                if (result.Changed)
                {
                    OnRegionChanged(result);
                }
                AnnounceRegion();
            }
            else
            {
                AnnounceCurrent(CellAxis.Row);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        /// <summary>
        /// Down past the last Details line flows into the Buttons region and Up returns; the Quests
        /// and Reward preferences list regions keep their hard edges.
        /// </summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return region == DetailsRegion;
        }

        /// <summary>
        /// Accept, Dismiss and the rest scroll with the quest text in vanilla's right pane rather
        /// than sitting in a footer, so they end the details instead of forming a fourth tab.
        /// </summary>
        protected override bool ActionsRegionInTabCycle
        {
            get { return false; }
        }

        protected override void RefreshContent()
        {
            quests = QuestMenuHelper.BuildQuestList(QuestMenuState.CurrentTab, QuestMenuState.EffectiveShowAll);

            Quest current = CurrentQuest();
            if (current != null)
            {
                detailLines = QuestMenuHelper.BuildDetailContentLines(current, DescriptionProvider(current));
                if (QuestMenuState.EffectiveShowDebugInfo)
                {
                    detailLines.AddRange(QuestMenuHelper.BuildDebugInfoLines(current));
                }
            }
            else
            {
                detailLines = new List<DetailLine>();
            }

            rewardPrefItems = QuestRewardHelper.GetRewardPreferenceItems();
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case QuestsRegion:
                    return "RimWorldAccess.Quests.Region.Quests".Translate();
                case DetailsRegion:
                    return "RimWorldAccess.Quests.Region.Details".Translate();
                default:
                    return "RimWorldAccess.Quests.Pref.Title".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case QuestsRegion:
                    // An empty tab keeps one read-only "no quests" row, so the region speaks a
                    // named placeholder instead of the shell's bare "empty".
                    return Math.Max(quests.Count, 1);
                case DetailsRegion:
                    return CurrentQuest() == null ? 0 : 1 + detailLines.Count;
                default:
                    return rewardPrefItems.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (region)
            {
                case QuestsRegion:
                    if (quests.Count == 0)
                    {
                        d.Label = "RimWorldAccess.Quests.Tab.NoQuests".Translate(
                            QuestMenuHelper.GetTabName(QuestMenuState.CurrentTab));
                        d.ReadOnly = true;
                        return d;
                    }
                    if (index < 0 || index >= quests.Count)
                    {
                        return d;
                    }
                    Quest rowQuest = quests[index];
                    d.Label = QuestMenuHelper.BuildQuestRowLabel(rowQuest);
                    d.Extras = QuestDescription(rowQuest);
                    return d;

                case DetailsRegion:
                    Quest quest = CurrentQuest();
                    if (quest == null)
                    {
                        return d;
                    }
                    if (index == 0)
                    {
                        d.Label = QuestMenuHelper.BuildDetailHeader(quest);
                        d.ReadOnly = true;
                        return d;
                    }
                    int lineIndex = index - 1;
                    if (lineIndex < 0 || lineIndex >= detailLines.Count)
                    {
                        return d;
                    }
                    DetailLine line = detailLines[lineIndex];
                    d.Label = line.Text;
                    if (line.Hyperlink.HasValue)
                    {
                        if (line.Hyperlink.Value.IsHidden)
                        {
                            d.ReadOnly = true;
                        }
                        else
                        {
                            d.Role = ElementRole.Button;
                        }
                    }
                    else if (line.InfoCardThing != null || line.InfoCardFaction != null)
                    {
                        // The mod-wide suffix for a carded row, not a "press Alt+I" instruction.
                        d.Extras = "RimWorldAccess.InfoCard.Inspectable".Translate().ToString().Trim();
                    }
                    else
                    {
                        d.ReadOnly = true;
                    }
                    return d;

                default:
                    if (index < 0 || index >= rewardPrefItems.Count)
                    {
                        return d;
                    }
                    return DescribeRewardPrefItem(rewardPrefItems[index]);
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case QuestsRegion:
                    {
                        if (index < 0 || index >= quests.Count)
                        {
                            return;
                        }
                        MoveResult result = Model.MoveToRegion(DetailsRegion);
                        if (result.Changed)
                        {
                            OnRegionChanged(result);
                        }
                        AnnounceRegion();
                        return;
                    }

                case DetailsRegion:
                    {
                        int lineIndex = index - 1;
                        if (lineIndex >= 0 && lineIndex < detailLines.Count && detailLines[lineIndex].Hyperlink.HasValue)
                        {
                            ActivateHyperlink(detailLines[lineIndex].Hyperlink.Value);
                            return;
                        }
                        // Other content lines have no Enter action of their own; just re-read the row.
                        AnnounceCurrentItem();
                        return;
                    }

                default:
                    {
                        if (index < 0 || index >= rewardPrefItems.Count)
                        {
                            return;
                        }
                        RewardPrefItem item = rewardPrefItems[index];
                        QuestRewardHelper.ToggleRewardPreference(item);
                        RefreshModel();
                        bool nowChecked = item.Type == RewardPrefType.RoyalFavor
                            ? item.Faction.allowRoyalFavorRewards
                            : item.Faction.allowGoodwillRewards;
                        var stateDesc = new ElementDescription();
                        stateDesc.Check = nowChecked ? CheckState.Checked : CheckState.Unchecked;
                        TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(stateDesc, TranslatedShellVocabulary.Instance));
                        return;
                    }
            }
        }

        /// <summary>
        /// Reward-preferences row, built like <see cref="RewardPrefsScope"/>'s rather than from
        /// <see cref="RewardPrefItem.Label"/>, which bakes the checked word into the string; Check
        /// state rides the Checkbox role instead.
        /// </summary>
        private ElementDescription DescribeRewardPrefItem(RewardPrefItem item)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Checkbox;
            if (item.Type == RewardPrefType.RoyalFavor)
            {
                d.Label = item.Faction.Name + ": " + "AcceptRoyalFavor".Translate(item.Faction.Named("FACTION")).CapitalizeFirst();
                d.Check = item.Faction.allowRoyalFavorRewards ? CheckState.Checked : CheckState.Unchecked;
                d.Extras = FlattenNewlines("AcceptRoyalFavorDesc".Translate(item.Faction.Named("FACTION")).Resolve().StripTags());
            }
            else
            {
                string relation = "RimWorldAccess.Quests.Pref.RelationPhrase".Translate(
                    item.Faction.PlayerGoodwill.ToStringWithSign(),
                    item.Faction.PlayerRelationKind.GetLabelCap()).ToString();
                d.Label = item.Faction.Name + ": " + "AcceptGoodwill".Translate().CapitalizeFirst() + ". " + relation;
                d.Check = item.Faction.allowGoodwillRewards ? CheckState.Checked : CheckState.Unchecked;
                d.Extras = FlattenNewlines("AcceptGoodwillDesc".Translate(item.Faction.Named("FACTION")).Resolve().StripTags());
            }
            return d;
        }

        /// <summary>
        /// A Quests row's supplementary half: description then rewards, in vanilla's own right-pane
        /// order. The description goes through the settle-aware provider the Details region uses, so
        /// one still being rewritten reads as its marker rather than mid-stream text.
        /// </summary>
        private string QuestDescription(Quest quest)
        {
            var builder = new AnnouncementBuilder();
            if (!quest.description.RawText.NullOrEmpty())
            {
                builder.Add(FlattenNewlines(DescriptionProvider(quest)().StripTags()));
            }
            builder.Add(QuestMenuHelper.BuildQuestRowRewards(quest));
            return builder.Build();
        }

        /// <summary>Vanilla's tooltip strings use literal "\n\n" paragraph breaks; announcements never use newlines as separators.</summary>
        private static string FlattenNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("\n\n", " ").Replace("\n", " ");
        }

        /// <summary>
        /// This scope is mirror-driven, not ScopeForWindow-attached, so it owns no window and the
        /// Buttons region is DeclaredActions alone. The real quests tab still drawing underneath is
        /// what <see cref="PointerSurface"/> names.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Alt+Shift+J routes off the real quests tab this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return QuestWindowRowRingPatch.HostWindow; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int i = 0; i < quests.Count; i++)
            {
                QuestWindowRowRingPatch.AddRouteCandidate(quests[i], QuestsRegion, i, candidates, targets);
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                Quest quest = CurrentQuest();
                if (quest == null)
                {
                    return actions;
                }

                if (quest.State == QuestState.NotYetAccepted)
                {
                    // ScreenAction has no "disabled" concept, so CanAcceptQuest is re-checked at
                    // activation time instead of here.
                    QuestPart_Choice choicePart = QuestRewardHelper.GetChoicePart(quest);
                    bool hasMultiChoice = choicePart != null && choicePart.choices.Count >= 2;

                    if (hasMultiChoice)
                    {
                        for (int i = 0; i < choicePart.choices.Count; i++)
                        {
                            int choiceIdx = i;
                            string rewardDesc = QuestRewardHelper.BuildRewardDescription(choicePart.choices[choiceIdx].rewards);
                            Quest localQuest = quest;
                            QuestPart_Choice localChoicePart = choicePart;
                            actions.Add(new ScreenAction(
                                "RimWorldAccess.Quests.Button.AcceptChoice".Translate(choiceIdx + 1, rewardDesc),
                                delegate { AcceptChoiceButtonAction(localQuest, localChoicePart, choiceIdx); }));
                        }
                    }
                    else
                    {
                        string acceptLabel = "AcceptButton".Translate();
                        if (choicePart != null && choicePart.choices.Count == 1)
                        {
                            string rewardDesc = QuestRewardHelper.BuildRewardDescription(choicePart.choices[0].rewards);
                            if (!string.IsNullOrEmpty(rewardDesc))
                            {
                                acceptLabel = "RimWorldAccess.Quests.Button.AcceptWithRewards".Translate(rewardDesc);
                            }
                        }
                        Quest acceptQuest = quest;
                        actions.Add(new ScreenAction(acceptLabel, delegate { QuestMenuState.AcceptQuest(acceptQuest); }));
                    }

                    Quest dismissQuest = quest;
                    actions.Add(new ScreenAction("RimWorldAccess.Quests.Button.Dismiss".Translate(),
                        delegate { QuestMenuState.ToggleDismissQuest(dismissQuest); }));
                }
                else if (quest.State == QuestState.Ongoing)
                {
                    Quest toggleQuest = quest;
                    actions.Add(new ScreenAction(
                        (quest.dismissed ? "RimWorldAccess.Quests.Button.Resume" : "RimWorldAccess.Quests.Button.Dismiss").Translate(),
                        delegate { QuestMenuState.ToggleDismissQuest(toggleQuest); }));
                }
                else if (quest.Historical)
                {
                    Quest deleteQuest = quest;
                    actions.Add(new ScreenAction("RimWorldAccess.Quests.Button.Delete".Translate(),
                        delegate { QuestMenuState.ToggleDismissQuest(deleteQuest); }));
                }

                var lookTargets = quest.QuestLookTargets.Where(t => CameraJumper.CanJump(t)).ToList();
                foreach (var target in lookTargets)
                {
                    GlobalTargetInfo localTarget = target;
                    string targetLabel = localTarget.Label;
                    string buttonLabel = string.IsNullOrEmpty(targetLabel)
                        ? (string)"RimWorldAccess.Quests.Button.JumpToLocation".Translate()
                        : (string)"RimWorldAccess.Quests.Button.JumpToTarget".Translate(targetLabel);
                    actions.Add(new ScreenAction(buttonLabel, delegate
                    {
                        CameraJumper.TryJumpAndSelect(localTarget);
                        QuestMenuState.Close();
                    }));
                }

                AddDevActions(quest);
                return actions;
            }
        }

        /// <summary>Re-checks CanAcceptQuest at activation time: the action is always declared and gated here.</summary>
        private void AcceptChoiceButtonAction(Quest quest, QuestPart_Choice choicePart, int choiceIdx)
        {
            AcceptanceReport canAccept = QuestUtility.CanAcceptQuest(quest);
            if (!canAccept.Accepted)
            {
                TolkHelper.Speak("RimWorldAccess.Quests.Action.CannotAcceptReason".Loc(canAccept.Reason), SpeechPriority.High);
                return;
            }
            QuestMenuState.AcceptQuestWithChoice(quest, choicePart, choicePart.choices[choiceIdx]);
        }

        /// <summary>Mirrors MainTabWindow_Quests' embedded dev widgets; labels are INTENTIONALLY raw literals (vanilla's own untranslated dev-tool text), not localized.</summary>
        private void AddDevActions(Quest quest)
        {
            if (QuestMenuState.DevAcceptButtonVisible(quest))
            {
                Quest devQuest = quest;
                actions.Add(new ScreenAction("DEV: Accept instantly", delegate { QuestMenuState.DevAcceptInstantly(devQuest); }));
            }

            if (QuestMenuState.DevToggleButtonsVisible)
            {
                actions.Add(new ScreenAction("DEV: Show all", delegate { QuestMenuState.ToggleDevShowAll(); RefreshModel(); }));
                actions.Add(new ScreenAction("DEV: Show debug info", delegate { QuestMenuState.ToggleDevShowDebugInfo(); RefreshModel(); }));
            }
        }
    }

    /// <summary>
    /// Keeps <see cref="QuestMenuScope"/> in lockstep with <see cref="QuestMenuState.IsActive"/>,
    /// reconciled every OnGUI pass by the shell dispatcher. MirrorReconcileOrder.Game.cs references
    /// this class and its Reconcile by name, so neither may be renamed.
    /// </summary>
    internal static class QuestMenuScopeMirror
    {
        private static readonly QuestMenuScope scope = new QuestMenuScope();

        public static void Reconcile()
        {
            // Runs every frame: a reward-choice float menu closed via mouse
            // click is cleaned up even with no following key press.
            if (QuestMenuState.HasActiveRewardMenu && !WindowlessFloatMenuState.IsActive)
            {
                QuestMenuState.CleanupRewardMenu();
            }

            // ForeignDialogWindowAbove is needed because the royal-favor accept path raises a
            // real Dialog_MessageBox confirmation while QuestMenuState stays active.
            if (QuestMenuState.IsActive && !InfoCardState.IsActive && !WindowlessFloatMenuState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Drives the real Quests window's selection every pass and resets the per-pass row-drawn flag
    /// <see cref="QuestWindowRowRingPatch"/> and <see cref="QuestWindowAutoScrollPatch"/> coordinate
    /// through. <see cref="MainTabWindow_Quests.Select"/> already sets the highlighted row and
    /// switches tabs, so this prefix only has to point it at the right quest.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoWindowContents))]
    internal static class QuestWindowSelectionPatch
    {
        /// <summary>
        /// Reset every pass and set by <see cref="QuestWindowRowRingPatch"/> when the focused row was
        /// actually drawn; <see cref="QuestWindowAutoScrollPatch"/> reads it to decide on a scroll.
        /// </summary>
        internal static bool focusedRowDrawn;

        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_Quests __instance)
        {
            try
            {
                focusedRowDrawn = false;
                Quest focused = FocusStackLookup.TopmostOfType<QuestMenuScope>()?.FocusedQuest;
                if (focused != null)
                {
                    __instance.Select(focused);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quest window selection error", ex);
            }
        }
    }

    /// <summary>
    /// <see cref="MainTabWindow_Quests.Select"/> is how every in-game quest link (letter option,
    /// world object, hyperlink, game condition, message) says "show me THIS quest", so arming
    /// <see cref="QuestMenuState.PendingSelectQuest"/> here is what opens the accessible screen on
    /// that quest instead of the first Available row. Two calls must NOT arm it:
    /// <see cref="QuestWindowSelectionPatch"/>'s per-pass Select (excluded by the session guard) and
    /// PreOpen's re-Select of the current selection (excluded by the unchanged-selection guard),
    /// which would otherwise land every ordinary tab open on the previous session's quest.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.Select))]
    internal static class QuestWindowSelectRequestPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Quest> selectedField =
            AccessTools.FieldRefAccess<MainTabWindow_Quests, Quest>("selected");

        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_Quests __instance, Quest quest)
        {
            try
            {
                if (quest == null || QuestMenuState.IsActive || quest == selectedField(__instance))
                {
                    return;
                }
                QuestMenuState.PendingSelectQuest = quest;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quest window select request error", ex);
            }
        }
    }

    /// <summary>
    /// Paints the keyboard focus ring over vanilla's own row highlight. Runs inside the same scroll
    /// view DrawQuest draws in, so rect needs no coordinate conversion.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "DoRow")]
    internal static class QuestWindowRowRingPatch
    {
        // Every drawn row, for pointer routing — the ring below needs only the focused one.
        private static readonly RowGeometryCache rowGeometry = new RowGeometryCache();

        /// <summary>The window whose GUI pass last drew the quest list, for pointer routing.</summary>
        internal static Window HostWindow
        {
            get { return rowGeometry.HostWindow; }
        }

        internal static void AddRouteCandidate(Quest quest, int region, int index,
            List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            rowGeometry.AddCandidate(quest, region, index, candidates, targets);
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, Quest quest)
        {
            try
            {
                QuestMenuScope scope = FocusStackLookup.TopmostOfType<QuestMenuScope>();
                if (scope == null)
                {
                    return;
                }
                rowGeometry.Record(quest, rect);
                if (quest != scope.FocusedQuest)
                {
                    return;
                }
                QuestWindowSelectionPatch.focusedRowDrawn = true;
                if (Event.current.type == EventType.Repaint)
                {
                    FocusRing.Draw(rect.ContractedBy(1f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quest window row ring error", ex);
            }
        }
    }

    /// <summary>
    /// Reads the private tmpQuestsToShow list right after SortQuestsByTab fills it — the only moment
    /// it holds the live draw order, since DoQuestsList clears it after drawing — and computes the
    /// focused quest's Y in the scroll view's 32px-per-row space.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "SortQuestsByTab")]
    internal static class QuestWindowRowIndexPatch
    {
        private static readonly AccessTools.FieldRef<List<Quest>> tmpQuestsToShowField =
            AccessTools.StaticFieldRefAccess<List<Quest>>(AccessTools.Field(typeof(MainTabWindow_Quests), "tmpQuestsToShow"));

        /// <summary>The focused quest's Y in the current tab's draw order, or -1 when it is not listed.</summary>
        internal static float focusedRowY = -1f;

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                focusedRowY = -1f;
                Quest focused = FocusStackLookup.TopmostOfType<QuestMenuScope>()?.FocusedQuest;
                if (focused == null)
                {
                    return;
                }
                List<Quest> questsToShow = tmpQuestsToShowField();
                if (questsToShow == null)
                {
                    return;
                }
                var visited = new HashSet<Quest>();
                float y = 0f;
                foreach (Quest quest in questsToShow)
                {
                    EmitRow(quest, questsToShow, visited, ref y, focused);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quest window row index error", ex);
            }
        }

        /// <summary>
        /// Mirrors DoQuestsList's local DrawQuest recursion for row-position bookkeeping only: an
        /// already-visited quest, or one whose parent is still pending, is skipped as vanilla skips
        /// it; a visit counts one 32px row and recurses into listed subquests. DRIFT RISK: keep in
        /// lockstep with vanilla's recursion.
        /// </summary>
        private static void EmitRow(Quest quest, List<Quest> questsToShow, HashSet<Quest> visited, ref float y, Quest focused)
        {
            if (visited.Contains(quest) || (quest.parent != null && questsToShow.Contains(quest.parent) && !visited.Contains(quest.parent)))
            {
                return;
            }
            if (quest == focused)
            {
                focusedRowY = y;
            }
            y += 32f;
            visited.Add(quest);
            foreach (Quest subquest in quest.GetSubquests())
            {
                if (questsToShow.Contains(subquest))
                {
                    EmitRow(subquest, questsToShow, visited, ref y, focused);
                }
            }
        }
    }

    /// <summary>
    /// Scrolls the focused quest into view when its row wasn't drawn this pass, picking the scroll
    /// field by the same dismissed/NotYetAccepted/Ongoing/else mapping
    /// <see cref="MainTabWindow_Quests.Select"/> uses to choose curTab.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoWindowContents))]
    internal static class QuestWindowAutoScrollPatch
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Vector2> scrollPositionAvailableField =
            AccessTools.FieldRefAccess<MainTabWindow_Quests, Vector2>("scrollPosition_available");
        private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Vector2> scrollPositionActiveField =
            AccessTools.FieldRefAccess<MainTabWindow_Quests, Vector2>("scrollPosition_active");
        private static readonly AccessTools.FieldRef<MainTabWindow_Quests, Vector2> scrollPositionHistoricalField =
            AccessTools.FieldRefAccess<MainTabWindow_Quests, Vector2>("scrollPosition_historical");

        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Quests __instance)
        {
            try
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                if (QuestWindowSelectionPatch.focusedRowDrawn || QuestWindowRowIndexPatch.focusedRowY < 0f)
                {
                    return;
                }
                Quest focused = FocusStackLookup.TopmostOfType<QuestMenuScope>()?.FocusedQuest;
                if (focused == null)
                {
                    return;
                }
                ScrollFocusedQuestIntoView(__instance, focused, QuestWindowRowIndexPatch.focusedRowY);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quest window auto-scroll error", ex);
            }
        }

        private static void ScrollFocusedQuestIntoView(MainTabWindow_Quests window, Quest quest, float y)
        {
            if (quest.dismissed)
            {
                ref Vector2 scroll = ref scrollPositionHistoricalField(window);
                scroll.y = y;
                return;
            }
            if (quest.State == QuestState.NotYetAccepted)
            {
                ref Vector2 scroll = ref scrollPositionAvailableField(window);
                scroll.y = y;
                return;
            }
            if (quest.State == QuestState.Ongoing)
            {
                ref Vector2 scroll = ref scrollPositionActiveField(window);
                scroll.y = y;
                return;
            }
            ref Vector2 fallback = ref scrollPositionHistoricalField(window);
            fallback.y = y;
        }
    }
}
