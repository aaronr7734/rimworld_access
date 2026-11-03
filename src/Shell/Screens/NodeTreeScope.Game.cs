using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the real vanilla <see cref="Dialog_NodeTree"/> family (comms
    /// negotiations, letters with choices, research-complete and game-end prompts). Attaches
    /// through the ScopeForWindow hierarchy mirror, so
    /// <see cref="Dialog_NodeTreeWithFactionInfo"/>, <see cref="Dialog_Negotiation"/>, and any
    /// modded subclass are covered without per-subclass registration.
    /// Vanilla gives this dialog NO keyboard behavior: closeOnAccept and closeOnCancel are both
    /// false and DoWindowContents never polls Return, so a mouse click inside a DiaOption's rect
    /// is the only way to progress. Every key here is therefore new, not a vanilla mirror.
    /// Two content regions: the node's body text one read-only row per line, then the DiaOption
    /// rows captured live from the real DrawNode pass. Activation reflects into the
    /// protected <c>DiaOption.Activate()</c>, the method a click would have invoked, so
    /// resolveTree/action/link semantics run unmodified. A node change is detected by comparing
    /// the protected curNode reference across passes — GotoNode reassigns the field in place, so
    /// there is no other signal.
    /// Enter is the chassis's Activate claim and OwnsAccept keeps the chassis default. No
    /// subclass in the family overrides OnAcceptKeyPressed/OnCancelKeyPressed, so the base
    /// Window-level routers already see every call. Owning accept still matters: otherwise a
    /// stray base-Window accept re-test could reach a different window entirely.
    /// The open announcement and each node change speak from the DrawNode pass, not the chassis's
    /// OnFocus seam: both need the option rows that pass captures, and the overlay guard below
    /// must be honored before anything speaks. The rows themselves announce through the chassis.
    /// </summary>
    public sealed class NodeTreeScope : ScreenScope
    {
        private const int ProseRegion = 0;
        private const int OptionsRegion = 1;

        private static readonly AccessTools.FieldRef<Dialog_NodeTree, DiaNode> curNodeField =
            AccessTools.FieldRefAccess<Dialog_NodeTree, DiaNode>("curNode");
        private static readonly AccessTools.FieldRef<Dialog_NodeTree, Vector2> optsScrollPositionField =
            AccessTools.FieldRefAccess<Dialog_NodeTree, Vector2>("optsScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_NodeTree, float> optTotalHeightField =
            AccessTools.FieldRefAccess<Dialog_NodeTree, float>("optTotalHeight");
        private static readonly AccessTools.FieldRef<Dialog_NodeTree, string> titleField =
            AccessTools.FieldRefAccess<Dialog_NodeTree, string>("title");
        private static readonly AccessTools.FieldRef<Dialog_NodeTreeWithFactionInfo, Faction> withFactionInfoFactionField =
            AccessTools.FieldRefAccess<Dialog_NodeTreeWithFactionInfo, Faction>("faction");
        private static readonly AccessTools.FieldRef<Dialog_Negotiation, Pawn> negotiatorField =
            AccessTools.FieldRefAccess<Dialog_Negotiation, Pawn>("negotiator");
        private static readonly AccessTools.FieldRef<Dialog_Negotiation, ICommunicable> commTargetField =
            AccessTools.FieldRefAccess<Dialog_Negotiation, ICommunicable>("commTarget");
        private static readonly PropertyInfo interactiveNowProperty =
            AccessTools.Property(typeof(Dialog_NodeTree), "InteractiveNow");
        private static readonly PropertyInfo marginProperty =
            AccessTools.Property(typeof(Window), "Margin");
        private static readonly MethodInfo activateMethod =
            AccessTools.Method(typeof(DiaOption), "Activate");

        private const float DefaultMargin = 18f;

        /// <summary>
        /// Tracks per DIALOG, not per scope instance, whether the open announcement has gone
        /// out: ScopeForWindow's re-attach arm can tear down and rebuild this scope while the
        /// same dialog stays open, and an instance field would either lose the announcement or
        /// re-speak it on a later frame, which the same-frame dedupe cannot catch. Keying on the
        /// dialog makes "exactly one open announcement" hold. ConditionalWeakTable self-clears,
        /// so no StateResetRegistry entry is needed.
        /// </summary>
        private static readonly ConditionalWeakTable<Dialog_NodeTree, object> openAnnouncedDialogs =
            new ConditionalWeakTable<Dialog_NodeTree, object>();

        private readonly Dialog_NodeTree dialog;
        private readonly List<DiaOptionRowCapture.CapturedOption> options =
            new List<DiaOptionRowCapture.CapturedOption>();
        private DiaNode lastNode;
        private bool pendingAnnounce;
        private string proseTextCache;
        private readonly List<string> proseLines = new List<string>();

        private static bool WasOpenAnnounced(Dialog_NodeTree forDialog)
        {
            object ignored;
            return openAnnouncedDialogs.TryGetValue(forDialog, out ignored);
        }

        private static void MarkOpenAnnounced(Dialog_NodeTree forDialog)
        {
            if (!WasOpenAnnounced(forDialog))
            {
                openAnnouncedDialogs.Add(forDialog, null);
            }
        }

        public NodeTreeScope(Dialog_NodeTree dialog)
        {
            this.dialog = dialog;

            Claim(SharedMenuGrammar.Cancel, OnCancel,
                when: delegate { return !TextDialogShared.ForeignWindowAbove(this.dialog); });
        }

        public override string Name
        {
            get { return "node-tree"; }
        }

        /// <summary>
        /// A hyperlink option's ActivateHyperlink() can open a scopeless Dialog_InfoCard on top
        /// without closing this dialog, leaving FocusStack.Top on this scope — so stand down as
        /// a modal while any scopeless, non-immediate window sits above.
        /// </summary>
        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>
        /// Escape is always this scope's: vanilla refuses to close on it, so there is nothing to
        /// fall through to. The base's Escape claim is registered first and clears a live search.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The options are the dialog's own Widgets.ButtonText calls and are already content rows above; nothing else here is a window button.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        internal bool IsFocusedOption(DiaOption option)
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != OptionsRegion || region == null || region.IsEmpty
                || region.Index < 0 || region.Index >= options.Count)
            {
                return false;
            }
            return ReferenceEquals(options[region.Index].Instance, option);
        }

        public override void OnPush()
        {
            base.OnPush();
            TooltipCapture.Arm();
        }

        public override void OnPop()
        {
            TooltipCapture.Disarm();
            base.OnPop();
        }

        public override void OnFocus()
        {
            // The DrawNode pass owns the first utterance: the counted option rows and the
            // settled row both come from its capture.
            SuppressNextEntryAnnouncement();
            base.OnFocus();
            pendingAnnounce = true;
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == ProseRegion
                ? (string)"Description".Translate()
                : (string)"Options".Translate();
        }

        protected override void RefreshContent()
        {
            options.Clear();
            IReadOnlyList<DiaOptionRowCapture.CapturedOption> captured = DiaOptionRowCapture.Items;
            for (int i = 0; i < captured.Count; i++)
            {
                options.Add(captured[i]);
            }
        }

        protected override bool ContentRegionsFlowVertically
        {
            get { return true; }
        }

        /// <summary>The node text as one row per non-empty line, re-split only when the text changes.</summary>
        private List<string> ProseLines()
        {
            string text = NodeText();
            if (!string.Equals(text, proseTextCache, StringComparison.Ordinal))
            {
                proseTextCache = text;
                proseLines.Clear();
                string[] raw = text.Split('\n');
                for (int i = 0; i < raw.Length; i++)
                {
                    string line = raw[i].Trim();
                    if (line.Length > 0)
                    {
                        proseLines.Add(line);
                    }
                }
            }
            return proseLines;
        }

        protected override int ContentItemCount(int region)
        {
            if (region == ProseRegion)
            {
                return ProseLines().Count;
            }
            return options.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == ProseRegion)
            {
                List<string> lines = ProseLines();
                return new ElementDescription
                {
                    Label = index >= 0 && index < lines.Count ? lines[index] : "",
                    ReadOnly = true,
                };
            }
            if (index < 0 || index >= options.Count)
            {
                return new ElementDescription();
            }
            DiaOptionRowCapture.CapturedOption option = options[index];
            var d = new ElementDescription();
            d.Label = option.Label;
            d.Role = ElementRole.Button;
            d.Disabled = option.Disabled;
            string tooltip = TooltipCapture.TryResolveAt(option.Rect);
            d.Extras = option.IsHyperlink
                ? CombineExtras(tooltip, "RimWorldAccess.UI.NodeTree.HyperlinkTag".Translate())
                : tooltip;
            return d;
        }

        /// <summary>Prose, not an item name — a stray word inside the node text must not outrank an option's own label.</summary>
        protected override bool ContentRegionSearchable(int region)
        {
            return region != ProseRegion;
        }

        /// <summary>The option's own label, without the tooltip the describe path resolves for speech.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (region != OptionsRegion || row < 0 || row >= options.Count)
            {
                return "";
            }
            return options[row].Label ?? "";
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ProseRegion)
            {
                AnnounceCurrentItem();
                return;
            }
            if (index < 0 || index >= options.Count || !CanActivate(index))
            {
                return;
            }
            ActivateOption(options[index]);
        }

        // Per-GUI-pass work, driven by DrawNode's own draw (NodeTreeDrawPatch below).
        internal void OnGuiPass(Rect rect)
        {
            DiaNode node = curNodeField(dialog);
            bool nodeChanged = lastNode == null || !ReferenceEquals(node, lastNode);
            RefreshModel();
            if (nodeChanged)
            {
                FocusFirstOption();
            }
            lastNode = node;
            AutoScrollToFocused(rect);

            if (ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                return;
            }
            if (pendingAnnounce)
            {
                pendingAnnounce = false;
                if (!WasOpenAnnounced(dialog))
                {
                    MarkOpenAnnounced(dialog);
                    TolkHelper.SpeakData(BuildOpenAnnouncement(node), SpeechPriority.High);
                }
                AnnounceCurrentItem();
            }
            else if (nodeChanged && WasOpenAnnounced(dialog))
            {
                // The conversation moved on: the header was already heard, so speak only the new
                // node text, then whatever focus reset to.
                TolkHelper.SpeakData(StripOrEmpty(node.text.Resolve()), SpeechPriority.High);
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Skips the re-readable body text onto the first real option; with no options at all
        /// the cursor stays on the prose.
        /// </summary>
        private void FocusFirstOption()
        {
            if (Model.IsRegionEmpty(OptionsRegion))
            {
                return;
            }
            Model.MoveToRegion(OptionsRegion);
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                region.MoveTo(0);
            }
        }

        /// <summary>
        /// Keeps the focused option visible. Replicates DrawNode's own viewport-height formula,
        /// since it hands the scroll view's rect straight to Widgets.BeginScrollView with no
        /// capturable call in between; Margin is reflected, so a modded override still counts.
        /// </summary>
        private void AutoScrollToFocused(Rect rect)
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != OptionsRegion || region == null || region.IsEmpty
                || region.Index < 0 || region.Index >= options.Count)
            {
                return;
            }
            float margin = DefaultMargin;
            if (marginProperty != null)
            {
                try
                {
                    margin = (float)marginProperty.GetValue(dialog, null);
                }
                catch (Exception)
                {
                    margin = DefaultMargin;
                }
            }
            float viewportHeight = Mathf.Min(optTotalHeightField(dialog), rect.height - 100f - margin * 2f);
            Rect optionRect = options[region.Index].Rect;
            ref Vector2 scroll = ref optsScrollPositionField(dialog);
            if (optionRect.y < scroll.y)
            {
                scroll.y = optionRect.y;
            }
            else if (optionRect.yMax > scroll.y + viewportHeight)
            {
                scroll.y = optionRect.yMax - viewportHeight;
            }
        }

        /// <summary>
        /// The delayInteractivity gate blocks vanilla's ButtonText branch but never its
        /// hyperlink branch, so a hyperlink option stays clickable during the delay and only
        /// button rows inherit it — during which this scope is as silent as vanilla's ignored
        /// click. A scopeless window above owns input, so nothing fires beneath it either.
        /// </summary>
        private bool CanActivate(int index)
        {
            if (TextDialogShared.ForeignWindowAbove(dialog))
            {
                return false;
            }
            return options[index].IsHyperlink || InteractiveNow();
        }

        private bool InteractiveNow()
        {
            if (interactiveNowProperty == null)
            {
                return true;
            }
            try
            {
                return (bool)interactiveNowProperty.GetValue(dialog, null);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private void ActivateOption(DiaOptionRowCapture.CapturedOption option)
        {
            if (option.IsHyperlink)
            {
                // Vanilla's hyperlink click path never routes through DiaOption.Activate(): it
                // makes this call directly and unconditionally. This is that same call.
                option.Instance.hyperlink.ActivateHyperlink();
                return;
            }
            if (option.Disabled)
            {
                // Vanilla renders this click as a silent no-op. Deliberate deviation: silence on
                // a deliberate keypress reads as a dead key, so speak the reason.
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.UI.Dialog.ButtonCannotActivate".Loc(option.Label));
                return;
            }
            activateMethod.Invoke(option.Instance, null);
            // Nothing else here on purpose: a resolveTree option's Activate() already closed the
            // dialog, and an action/link option changed curNode for the next OnGuiPass to
            // announce. Either way this handler has nothing left to say.
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // Shields every OTHER window from vanilla's unconditional per-window cancel re-test
            // for the rest of this frame: one stamp short-circuits all OnCancelKeyPressed calls.
            ShellFrameStamps.MarkCancelConsumed();
            // Escape re-reads the current node rather than dismissing the conversation, which
            // vanilla itself forbids and which would lose a choice that matters.
            TolkHelper.SpeakData(DialogFrame.Description("", NodeText()), SpeechPriority.High);
        }

        private static string CombineExtras(string tooltip, string tag)
        {
            return string.IsNullOrEmpty(tooltip) ? tag : tooltip + ". " + tag;
        }

        /// <summary>
        /// The shared DialogFrame.Opened shape: dialog word, header and body text, then the
        /// NavInstructions count-and-hints tail every dialog family carries.
        /// BuildHeader already ends in ". ", the separator DialogFrame.Opened adds after a title,
        /// so that one trailing separator is stripped before the header is passed as the title.
        /// </summary>
        private string BuildOpenAnnouncement(DiaNode node)
        {
            string header = BuildHeader();
            string title = string.IsNullOrEmpty(header) ? "" : header.Substring(0, header.Length - 2);
            string nodeText = StripOrEmpty(node.text.Resolve());
            return DialogFrame.Opened(title, nodeText, options.Count);
        }

        /// <summary>
        /// Dialog_Negotiation overrides DoWindowContents and never draws a title, so it gets a
        /// header built from the facts vanilla does draw — negotiator, comm target, social skill,
        /// relation — instead of the generic title path.
        /// </summary>
        private string BuildHeader()
        {
            Dialog_Negotiation negotiation = dialog as Dialog_Negotiation;
            if (negotiation != null)
            {
                return BuildNegotiationHeader(negotiation);
            }

            string header = "";
            string title = StripOrEmpty(titleField(dialog));
            if (title.Length > 0)
            {
                header += title + ". ";
            }

            Dialog_NodeTreeWithFactionInfo withFaction = dialog as Dialog_NodeTreeWithFactionInfo;
            if (withFaction != null)
            {
                Faction faction = withFactionInfoFactionField(withFaction);
                if (faction != null && !faction.Hidden)
                {
                    header += faction.Name + ". " + faction.PlayerRelationKind.GetLabelCap() + ". ";
                }
            }
            return header;
        }

        private string BuildNegotiationHeader(Dialog_Negotiation negotiation)
        {
            Pawn negotiator = negotiatorField(negotiation);
            ICommunicable commTarget = commTargetField(negotiation);
            string header = negotiator.LabelCap + ". " + commTarget.GetCallLabel() + ". ";
            header += "SocialSkillIs".Translate(negotiator.skills.GetSkill(SkillDefOf.Social).Level) + ". ";
            Faction faction = commTarget.GetFaction();
            if (faction != null)
            {
                header += faction.PlayerRelationKind.GetLabelCap() + ". ";
            }
            return header;
        }

        private string NodeText()
        {
            DiaNode node = curNodeField(dialog);
            return node == null ? "" : StripOrEmpty(node.text.Resolve());
        }

        private static string StripOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.StripTags();
        }
    }

    /// <summary>
    /// Brackets DiaOptionRowCapture/TooltipCapture to DrawNode's own pass, and lets the scope
    /// refresh, auto-scroll, and fire deferred announcements inside the window's GUI pass.
    /// DrawNode is the one bracket point that covers the whole family: Dialog_Negotiation's
    /// override never calls base, but it does call DrawNode directly.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_NodeTree), "DrawNode")]
    public static class NodeTreeDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_NodeTree __instance, out NodeTreeScope __state)
        {
            __state = null;
            try
            {
                NodeTreeScope scope = FocusStack.Top as NodeTreeScope;
                if (scope != null && scope.Owns(__instance))
                {
                    __state = scope;
                    DiaOptionRowCapture.BeginPass();
                    TooltipCapture.BeginPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Node tree draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(Rect rect, Exception __exception, NodeTreeScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, rect, ReferenceEquals(FocusStack.Top, __state), __exception,
                    () => { DiaOptionRowCapture.EndPass(); TooltipCapture.EndPass(); }, (s, r) => s.OnGuiPass(r), "Node tree draw pass error");
            }
        }
    }
}
