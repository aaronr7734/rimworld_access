using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Narrative Feed's Dialogue Log: a self-drawn <see cref="Window"/> paired with
    /// <see cref="DialogueLogScope"/>, the same shape as
    /// <see cref="Dialog_ConfigureAnnouncements"/>/<see cref="AnnouncementConfigScope"/>. Nothing to
    /// wrap: no sighted-equivalent screen exists in Bubbles or RimTalk. Registered in ShellBootstrap;
    /// opened from the F12 extras hub whenever Bubbles or RimTalk is active, and behind the reserved,
    /// unbound <c>narrative.openDialogueLog</c> action id.
    /// </summary>
    public sealed class DialogueLogWindow : Window
    {
        private Vector2 scrollPosition;

        public DialogueLogWindow()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(720f, 560f); }
        }

        /// <summary>
        /// Visual mirror of <see cref="DialogueLogScope"/>'s content: reads the SAME
        /// <see cref="DialogueLogScope.Rows"/> list (never a re-fetch of the feed) so a sighted
        /// companion sees what the keyboard cursor reads, highlighting the cursor's row. A left-click
        /// moves the cursor there and reads it (<see cref="DialogueLogScope.SelectRow"/>), so the
        /// window is never a second, mouse-only surface. The action buttons read their label and
        /// enabled state from the scope and invoke its methods, so mouse and keyboard cannot drift.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            DialogueLogScope scope = DialogueLogScope.Current;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight),
                (string)"RimWorldAccess.Narrative.DialogueLog.Title".Translate());
            Text.Font = GameFont.Small;

            const float buttonHeight = 35f;
            float y = inRect.y + titleHeight + 8f;
            float buttonRowY = inRect.yMax - buttonHeight;

            Rect listRect = new Rect(inRect.x, y, inRect.width, buttonRowY - 10f - y);
            DrawList(listRect, scope);
            DrawButtons(new Rect(inRect.x, buttonRowY, inRect.width, buttonHeight), scope);
        }


        private void DrawList(Rect outer, DialogueLogScope scope)
        {
            IReadOnlyList<NarrativeRecord> rows = scope != null ? scope.Rows : null;
            if (rows == null || rows.Count == 0)
            {
                Widgets.Label(outer, (string)"RimWorldAccess.Narrative.DialogueLog.Empty".Translate());
                return;
            }

            int highlighted = scope != null ? scope.CurrentRowIndex : -1;
            float innerWidth = outer.width - 16f;
            var heights = new float[rows.Count];
            float totalHeight = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                float h = Text.CalcHeight(DialogueLogScope.ComposeLineWithTimeAgo(rows[i]), innerWidth) + 4f;
                heights[i] = h;
                totalHeight += h;
            }

            Rect viewRect = new Rect(0f, 0f, innerWidth, totalHeight);
            Widgets.BeginScrollView(outer, ref scrollPosition, viewRect);
            float curY = 0f;
            int clickedIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect rowRect = new Rect(0f, curY, innerWidth, heights[i]);
                if (i == highlighted)
                {
                    Widgets.DrawHighlight(rowRect);
                }
                Widgets.Label(rowRect, DialogueLogScope.ComposeLineWithTimeAgo(rows[i]));
                // A left-click anywhere on the row moves the keyboard cursor here and reads it.
                // Deferred until after the loop so acting mid-loop cannot disturb this frame's layout.
                if (Widgets.ButtonInvisible(rowRect))
                {
                    clickedIndex = i;
                }
                curY += heights[i];
            }
            Widgets.EndScrollView();

            if (clickedIndex >= 0 && scope != null)
            {
                scope.SelectRow(clickedIndex);
            }
        }

        private void DrawButtons(Rect rect, DialogueLogScope scope)
        {
            var labels = new List<string>(4);
            var actions = new List<Action>(4);
            var enabledFlags = new List<bool>(4);

            bool jumpEnabled = scope != null && scope.JumpEnabled;
            labels.Add((string)"RimWorldAccess.Narrative.DialogueLog.JumpToSpeaker".Translate());
            actions.Add(delegate { scope?.JumpToSpeaker(); });
            enabledFlags.Add(jumpEnabled);

            if (scope != null && scope.RimTalkPresent)
            {
                bool overlayOn = scope.OverlayEnabled;
                labels.Add(overlayOn
                    ? (string)"RimWorldAccess.Narrative.DialogueLog.ToggleOverlayOff".Translate()
                    : (string)"RimWorldAccess.Narrative.DialogueLog.ToggleOverlayOn".Translate());
                actions.Add(delegate { scope.ToggleOverlay(); });
                enabledFlags.Add(true);

                AddToolbarVariantButtons("rimtalk.toggle", labels, actions, enabledFlags);
            }

            // Bubbles may be installed without RimTalk, so its toolbar variants are gated
            // independently of the block above.
            AddToolbarVariantButtons("bubbles.toggle", labels, actions, enabledFlags);

            // The RimTalk TTS addon's overlay-adjacent controls. Each gate mirrors the addon's own
            // draw gate (RimTalkTtsOverlayCompat), so a button appears iff its mouse control would.
            if (scope != null)
            {
                bool ttsOnVisible = RimTalkTtsOverlayCompat.TryGetOnState(out bool ttsIsOn);
                if (ttsOnVisible)
                {
                    labels.Add(ttsIsOn
                        ? (string)"RimWorldAccess.Narrative.DialogueLog.TtsToggleOff".Translate()
                        : (string)"RimWorldAccess.Narrative.DialogueLog.TtsToggleOn".Translate());
                    actions.Add(delegate { scope.ToggleTtsOnState(); });
                    enabledFlags.Add(true);
                }

                if (RimTalkTtsOverlayCompat.ControlButtonsAvailable())
                {
                    labels.Add((string)"RimWorldAccess.Narrative.DialogueLog.TtsResetAudio".Translate());
                    actions.Add(delegate { scope.TtsResetAudio(); });
                    enabledFlags.Add(true);

                    labels.Add((string)"RimWorldAccess.Narrative.DialogueLog.TtsGenerateDialogue".Translate());
                    actions.Add(delegate { scope.TtsGenerateDialogue(); });
                    enabledFlags.Add(true);

                    labels.Add((string)"RimWorldAccess.Narrative.DialogueLog.TtsIgnoreAll".Translate());
                    actions.Add(delegate { scope.TtsIgnoreAllDialogue(); });
                    enabledFlags.Add(true);

                    labels.Add((string)"RimWorldAccess.Narrative.DialogueLog.TtsDisplayNext".Translate());
                    actions.Add(delegate { scope.TtsDisplayNextDialogue(); });
                    enabledFlags.Add(true);
                }
            }

            labels.Add((string)"CloseButton".Translate());
            actions.Add(delegate { Close(); });
            enabledFlags.Add(true);

            const float spacing = 10f;
            float width = (rect.width - spacing * (labels.Count - 1)) / labels.Count;
            float x = rect.x;
            int focusedIndex = scope != null ? scope.CurrentActionIndex : -1;
            for (int i = 0; i < labels.Count; i++)
            {
                Rect r = new Rect(x, rect.y, width, rect.height);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = enabledFlags[i];
                if (Widgets.ButtonText(r, labels[i]))
                {
                    actions[i]();
                }
                GUI.enabled = wasEnabled;
                if (i == focusedIndex)
                {
                    FocusRing.Draw(r);
                }
                x += width + spacing;
            }
        }

        /// <summary>
        /// Appends every currently-available modifier variant of a mod's PlaySettings toolbar icon as
        /// its own visual button, running the SAME Activate delegate a keyboard press would.
        /// </summary>
        private static void AddToolbarVariantButtons(string iconId, List<string> labels, List<Action> actions, List<bool> enabledFlags)
        {
            foreach (ToolbarModifierRegistry.Variant variant in ToolbarModifierRegistry.AvailableVariants(iconId))
            {
                Action activate = variant.Activate;
                labels.Add(variant.LabelKey.Translate().ToString());
                actions.Add(delegate { activate(); });
                enabledFlags.Add(true);
            }
        }
    }

    /// <summary>
    /// Keyboard scope for <see cref="DialogueLogWindow"/>. One content region ("Lines") over
    /// <see cref="NarrativeFeed.Core"/>'s snapshot, NEWEST FIRST, each row "{time-ago}. {speaker/
    /// recipient/text grammar}" (<see cref="ComposeLineWithTimeAgo"/>). A "New conversation." marker
    /// is appended only when the just-landed row's ConversationId differs from wherever the cursor was
    /// BEFORE the move (tracked in <see cref="OnCursorSettled"/>, never recomputed by adjacency). The
    /// empty state is one placeholder row rather than an empty region, so Tab never skips this screen:
    /// <see cref="ContentItemCount"/> reports 1 while the real list is empty.
    ///
    /// LIVE REFRESH: subscribed to <see cref="NarrativeFeed.FeedChanged"/> for the scope's open
    /// lifetime (OnPush/OnPop, bounded by the window, so no StateResetRegistry entry). A fired event
    /// snapshots the focused row's DedupeKey, refreshes, and re-resolves the cursor by that key —
    /// never hold NarrativeRecord references across a rebuild that can evict them. An evicted row
    /// leaves the cursor wherever <see cref="ScreenModel"/> clamps it, silently.
    ///
    /// BUTTONS: <see cref="CaptureWindowButtons"/> is false — the window's plain ButtonText calls
    /// carry no disabled-state semantics to mirror, and "Jump to speaker" needs one — so
    /// <see cref="DeclaredActions"/> supplies every conditional action plus Close, and the window's
    /// draw rebuilds the identical state and invokes the SAME delegates.
    /// <list type="bullet">
    /// <item>"Jump to speaker" — always present, disabled with a reason unless the focused row's
    /// speaker resolves live and is reachable, mirroring RimTalk's own overlay clickable name
    /// (<c>UIUtil.DrawClickablePawnName</c>): a dead pawn jumps to a spawned corpse, a live one jumps
    /// if spawned, and the jump is <see cref="CameraJumper.TryJump"/> WITHOUT a select call, because
    /// the overlay's own click does not select either.</item>
    /// <item>"Toggle chat overlay" — only when RimTalk is active; rides
    /// <see cref="RimTalkNarrativeCompat"/>'s vehicle B.</item>
    /// <item>Toolbar modifier variants — the keyboard equivalent of holding a modifier during that
    /// icon's own PlaySettings click. Declared generically via
    /// <see cref="ToolbarModifierRegistry.AvailableVariants"/>; the registrations live in the compat
    /// classes, each a vehicle A call. Bubbles' variants are gated independently of RimTalk's, since
    /// RimTalk hard-depends on Bubbles and not the reverse.</item>
    /// <item>RimTalk TTS addon controls — present only while the addon is installed, active, and its
    /// own matching mouse control would be visible (<see cref="RimTalkTtsOverlayCompat"/> mirrors each
    /// draw gate). "Turn on/off TTS" is a first-class action rather than a modifier variant; the four
    /// map-view buttons each invoke the exact private delegate the mouse click runs.</item>
    /// </list>
    ///
    /// Escape is left at the <see cref="ScreenScope"/> default: an ordinary transient dialog whose
    /// Escape reaches vanilla's own <see cref="Window.OnCancelKeyPressed"/> and closes it.
    /// </summary>
    public sealed class DialogueLogScope : ScreenScope
    {
        private const int LinesRegion = 0;
        private const string RimTalkPackageId = "cj.rimtalk";

        /// <summary>The currently-open instance, for the window's mouse-driven buttons to reach into. Set in OnPush, cleared in OnPop.</summary>
        internal static DialogueLogScope Current { get; private set; }

        private readonly DialogueLogWindow window;
        private readonly List<NarrativeRecord> rows = new List<NarrativeRecord>();
        private readonly List<ScreenAction> actionsBuffer = new List<ScreenAction>();
        private bool announcedOpen;

        private int lastFocusedConversationId = int.MinValue;
        private int newConversationMarkerIndex = -1;

        public DialogueLogScope(DialogueLogWindow window)
        {
            this.window = window;
        }

        public override string Name
        {
            get { return "dialogue-log"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
            NarrativeFeed.FeedChanged += OnFeedChanged;
        }

        public override void OnPop()
        {
            NarrativeFeed.FeedChanged -= OnFeedChanged;
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;
            // Seed the conversation-boundary baseline to the row about to be announced, silently:
            // without it the int.MinValue sentinel makes the first arrow press read "new
            // conversation" even when landing inside the same one.
            int initialIndex = CurrentRowIndex;
            lastFocusedConversationId = (initialIndex >= 0 && initialIndex < rows.Count)
                ? rows[initialIndex].ConversationId
                : int.MinValue;
            newConversationMarkerIndex = -1;
            TolkHelper.SpeakData(rows.Count == 1
                ? (string)"RimWorldAccess.Narrative.DialogueLog.OpenedOne".Translate()
                : (string)"RimWorldAccess.Narrative.DialogueLog.Opened".Translate(rows.Count));
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Keeps the cursor on the same logical line across a background rebuild: snapshots the
        /// focused row's DedupeKey before <see cref="RefreshModel"/> rebuilds <see cref="rows"/>, then
        /// re-resolves to wherever that key landed (indices shift as newer lines arrive). Silent.
        /// </summary>
        private void OnFeedChanged()
        {
            string focusedKey = CurrentRow()?.DedupeKey;
            RefreshModel();
            if (focusedKey == null)
            {
                return;
            }
            int newIndex = rows.FindIndex(r => r.DedupeKey == focusedKey);
            if (newIndex < 0)
            {
                return;
            }
            ListModel region = Model.Region(LinesRegion);
            if (region == null)
            {
                return;
            }
            region.MoveTo(newIndex);
            // Recompute the boundary marker at the new index — idempotent per OnCursorSettled.
            OnCursorSettled(LinesRegion, newIndex);
        }

        // Content: the Lines region.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"RimWorldAccess.Narrative.DialogueLog.LinesRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count == 0 ? 1 : rows.Count;
        }

        protected override void RefreshContent()
        {
            IReadOnlyList<NarrativeRecord> snapshot = NarrativeFeed.Core.Snapshot();
            rows.Clear();
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                rows.Add(snapshot[i]); // oldest-last snapshot -> newest-first rows.
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription { ReadOnly = true };
            if (rows.Count == 0)
            {
                d.Label = (string)"RimWorldAccess.Narrative.DialogueLog.Empty".Translate();
                return d;
            }
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            NarrativeRecord record = rows[index];
            d.Label = ComposeLineWithTimeAgo(record);
            d.PositionIndex = index + 1;
            d.PositionCount = rows.Count;
            if (index == newConversationMarkerIndex)
            {
                d.Extras = (string)"RimWorldAccess.Narrative.DialogueLog.NewConversation".Translate();
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem(); // Read-only row: no action of its own.
        }

        /// <summary>
        /// The conversation-boundary tracker: fires after the cursor lands anywhere, before the
        /// announcement composes. Compares the just-landed row's ConversationId to wherever the cursor
        /// was BEFORE this move (never to the row's list neighbour), so a marker speaks exactly once
        /// per actual crossing. Cheap, silent, idempotent, and calls no RefreshModel.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != LinesRegion || rows.Count == 0 || index < 0 || index >= rows.Count)
            {
                newConversationMarkerIndex = -1;
                return;
            }
            int conversationId = rows[index].ConversationId;
            newConversationMarkerIndex = (conversationId != -1 && conversationId != lastFocusedConversationId) ? index : -1;
            lastFocusedConversationId = conversationId;
        }

        /// <summary>The row the keyboard cursor currently sits on, or null (empty region, cursor elsewhere, or the empty-state placeholder).</summary>
        private NarrativeRecord CurrentRow()
        {
            int idx = CurrentRowIndex;
            return (idx >= 0 && idx < rows.Count) ? rows[idx] : null;
        }

        /// <summary>
        /// A mouse click on a row in <see cref="DialogueLogWindow"/>'s list: moves the keyboard cursor
        /// there and reads it, as if arrowed to — one shared cursor drives both input methods, so a
        /// click is never a second, silent selection channel.
        /// </summary>
        internal void SelectRow(int index)
        {
            if (index >= 0 && index < rows.Count)
            {
                LandCursorAt(LinesRegion, index);
            }
        }

        /// <summary>
        /// The selected row index within the Lines region, or -1 — read by the window to draw the
        /// sighted-companion highlight and by the Buttons region to pick its target line. Reads the
        /// Lines region's own retained cursor, not the model's current region, so Tabbing into Buttons
        /// keeps acting on the last focused line.
        /// </summary>
        internal int CurrentRowIndex
        {
            get
            {
                // Guarded: RefreshModel queries the Buttons region's enablement (DeclaredActions ->
                // JumpEnabled -> here) before the region list is rebuilt, so the model can have none.
                if (Model.RegionCount <= LinesRegion)
                {
                    return -1;
                }
                ListModel region = Model.Region(LinesRegion);
                return region != null && !region.IsEmpty ? region.Index : -1;
            }
        }

        /// <summary>
        /// The DeclaredActions index the keyboard cursor sits on, or -1 — read by the window to ring
        /// the focused button. <see cref="CaptureWindowButtons"/> is false, so the shared
        /// <see cref="FocusedCaptureIndex"/> machinery never populates here; this closed-forms
        /// <c>InActionsRegion</c> for this scope's fixed shape (one content region, no captured-extras
        /// region), where Buttons is always the region immediately after content.
        /// </summary>
        internal int CurrentActionIndex
        {
            get
            {
                if (Model.RegionIndex != ContentRegionCount)
                {
                    return -1;
                }
                ListModel region = Model.CurrentRegion;
                return (region != null && !region.IsEmpty) ? region.Index : -1;
            }
        }

        /// <summary>Snapshot of this pass's rows, for the window's own read-only visual mirror.</summary>
        internal IReadOnlyList<NarrativeRecord> Rows
        {
            get { return rows; }
        }

        /// <summary>"{time-ago}. {speaker/recipient/text grammar}" -- shared by the spoken row and the window's own visual row, so they can never show different text for the same line.</summary>
        internal static string ComposeLineWithTimeAgo(NarrativeRecord record)
        {
            int ageTicks = GenTicks.TicksGame - record.Tick;
            if (ageTicks < 0)
            {
                ageTicks = 0;
            }
            // The same vanilla vehicle PawnLogAdapter uses for its own log timestamps.
            string timeAgo = ageTicks.ToStringTicksToPeriod();
            return (string)"RimWorldAccess.Narrative.DialogueLog.LineWithTimeAgo".Translate(timeAgo, NarrativeFeed.ComposeLine(record));
        }

        // Buttons region.

        internal bool RimTalkPresent
        {
            get { return ModsConfig.IsActive(RimTalkPackageId); }
        }

        internal bool OverlayEnabled
        {
            get
            {
                bool enabled;
                return RimTalkNarrativeCompat.TryGetOverlayEnabled(out enabled) && enabled;
            }
        }

        /// <summary>Mirrors RimTalk's own overlay clickable-name gate (UIUtil.DrawClickablePawnName): a dead pawn needs a spawned corpse, a live one needs to be spawned itself.</summary>
        internal bool JumpEnabled
        {
            get
            {
                Pawn speaker = ResolveSpeaker(CurrentRow());
                if (speaker == null)
                {
                    return false;
                }
                return speaker.Dead
                    ? (speaker.Corpse != null && speaker.Corpse.Spawned)
                    : speaker.Spawned;
            }
        }

        private static Pawn ResolveSpeaker(NarrativeRecord record)
        {
            return record?.SpeakerRef?.Target as Pawn;
        }

        /// <summary>
        /// Vehicle A: the exact call RimTalk's overlay clickable name makes —
        /// <see cref="CameraJumper.TryJump"/> with NO select, since the overlay's click does not select.
        /// </summary>
        internal void JumpToSpeaker()
        {
            Pawn speaker = ResolveSpeaker(CurrentRow());
            if (speaker == null)
            {
                AnnounceCurrentItem();
                return;
            }
            if (speaker.Dead && speaker.Corpse != null && speaker.Corpse.Spawned)
            {
                CameraJumper.TryJump(speaker.Corpse, CameraJumper.MovementMode.Pan);
            }
            else if (!speaker.Dead && speaker.Spawned)
            {
                CameraJumper.TryJump(speaker, CameraJumper.MovementMode.Pan);
            }
            else
            {
                AnnounceCurrentItem();
                return;
            }
            MapNavigationState.SpeakJumpedTo(speaker.LabelShort);
        }

        /// <summary>Vehicle B: RimTalk's own field-then-Write() pair (see <see cref="RimTalkNarrativeCompat.TrySetOverlayEnabled"/>).</summary>
        internal void ToggleOverlay()
        {
            bool current;
            if (!RimTalkNarrativeCompat.TryGetOverlayEnabled(out current) || !RimTalkNarrativeCompat.TrySetOverlayEnabled(!current))
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
                return;
            }
            TolkHelper.SpeakData(!current
                ? (string)"RimWorldAccess.Narrative.DialogueLog.OverlayTurnedOn".Translate()
                : (string)"RimWorldAccess.Narrative.DialogueLog.OverlayTurnedOff".Translate());
        }

        /// <summary>MUTATION-C: see <see cref="RimTalkTtsOverlayCompat"/>'s class header for why the addon's own TogglePatch cannot be invoked directly.</summary>
        internal void ToggleTtsOnState()
        {
            bool current;
            if (!RimTalkTtsOverlayCompat.TryGetOnState(out current) || !RimTalkTtsOverlayCompat.TrySetOnState(!current))
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
                return;
            }
            TolkHelper.SpeakData(!current
                ? (string)"RimWorldAccess.Narrative.DialogueLog.TtsTurnedOn".Translate()
                : (string)"RimWorldAccess.Narrative.DialogueLog.TtsTurnedOff".Translate());
        }

        /// <summary>Vehicle A: invokes OverlayButtonPatch's own "Audio clear" delegate (see <see cref="RimTalkTtsOverlayCompat.TryResetAudio"/>).</summary>
        internal void TtsResetAudio()
        {
            RunTtsControlAction(RimTalkTtsOverlayCompat.TryResetAudio, "RimWorldAccess.Narrative.DialogueLog.TtsResetComplete");
        }

        /// <summary>Vehicle A: invokes OverlayButtonPatch's own "Generate dialogue" delegate (see <see cref="RimTalkTtsOverlayCompat.TryGenerateDialogue"/>).</summary>
        internal void TtsGenerateDialogue()
        {
            RunTtsControlAction(RimTalkTtsOverlayCompat.TryGenerateDialogue, "RimWorldAccess.Narrative.DialogueLog.TtsGenerateStarted");
        }

        /// <summary>Vehicle A: invokes OverlayButtonPatch's own "Ignore existing dialogues" delegate (see <see cref="RimTalkTtsOverlayCompat.TryIgnoreAllDialogue"/>).</summary>
        internal void TtsIgnoreAllDialogue()
        {
            RunTtsControlAction(RimTalkTtsOverlayCompat.TryIgnoreAllDialogue, "RimWorldAccess.Narrative.DialogueLog.TtsIgnoreComplete");
        }

        /// <summary>Vehicle A: invokes OverlayButtonPatch's own "Display next" delegate (see <see cref="RimTalkTtsOverlayCompat.TryDisplayNextDialogue"/>).</summary>
        internal void TtsDisplayNextDialogue()
        {
            RunTtsControlAction(RimTalkTtsOverlayCompat.TryDisplayNextDialogue, "RimWorldAccess.Narrative.DialogueLog.TtsDisplayStarted");
        }

        private static void RunTtsControlAction(Func<bool> action, string successKey)
        {
            TolkHelper.SpeakData(action()
                ? (string)successKey.Translate()
                : (string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actionsBuffer.Clear();

                bool jumpEnabled = JumpEnabled;
                actionsBuffer.Add(new ScreenAction(
                    (string)"RimWorldAccess.Narrative.DialogueLog.JumpToSpeaker".Translate(),
                    JumpToSpeaker,
                    disabled: !jumpEnabled,
                    disabledReason: jumpEnabled ? null : (string)"RimWorldAccess.Narrative.DialogueLog.JumpDisabledReason".Translate()));

                if (RimTalkPresent)
                {
                    bool overlayOn = OverlayEnabled;
                    actionsBuffer.Add(new ScreenAction(
                        overlayOn
                            ? (string)"RimWorldAccess.Narrative.DialogueLog.ToggleOverlayOff".Translate()
                            : (string)"RimWorldAccess.Narrative.DialogueLog.ToggleOverlayOn".Translate(),
                        ToggleOverlay));

                    AddToolbarVariantActions("rimtalk.toggle");
                }

                // Bubbles may be installed without RimTalk, so its variants are gated independently.
                AddToolbarVariantActions("bubbles.toggle");

                // The RimTalk TTS addon's controls; each gate mirrors the addon's own draw gate,
                // independent of RimTalkPresent (the addon hard-depends on RimTalk core).
                bool ttsOnVisible = RimTalkTtsOverlayCompat.TryGetOnState(out bool ttsIsOn);
                if (ttsOnVisible)
                {
                    actionsBuffer.Add(new ScreenAction(
                        ttsIsOn
                            ? (string)"RimWorldAccess.Narrative.DialogueLog.TtsToggleOff".Translate()
                            : (string)"RimWorldAccess.Narrative.DialogueLog.TtsToggleOn".Translate(),
                        ToggleTtsOnState));
                }

                if (RimTalkTtsOverlayCompat.ControlButtonsAvailable())
                {
                    actionsBuffer.Add(new ScreenAction((string)"RimWorldAccess.Narrative.DialogueLog.TtsResetAudio".Translate(), TtsResetAudio));
                    actionsBuffer.Add(new ScreenAction((string)"RimWorldAccess.Narrative.DialogueLog.TtsGenerateDialogue".Translate(), TtsGenerateDialogue));
                    actionsBuffer.Add(new ScreenAction((string)"RimWorldAccess.Narrative.DialogueLog.TtsIgnoreAll".Translate(), TtsIgnoreAllDialogue));
                    actionsBuffer.Add(new ScreenAction((string)"RimWorldAccess.Narrative.DialogueLog.TtsDisplayNext".Translate(), TtsDisplayNextDialogue));
                }

                actionsBuffer.Add(new ScreenAction(
                    (string)"CloseButton".Translate(), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actionsBuffer;
            }
        }

        /// <summary>
        /// Appends every currently-available modifier variant of a mod's PlaySettings toolbar icon as
        /// its own declared action — the keyboard equivalent of holding that modifier during the
        /// icon's own click, which a keyboard user cannot otherwise reach.
        /// </summary>
        private void AddToolbarVariantActions(string iconId)
        {
            foreach (ToolbarModifierRegistry.Variant variant in ToolbarModifierRegistry.AvailableVariants(iconId))
            {
                Action activate = variant.Activate;
                actionsBuffer.Add(new ScreenAction(variant.LabelKey.Translate().ToString(), activate));
            }
        }
    }
}
