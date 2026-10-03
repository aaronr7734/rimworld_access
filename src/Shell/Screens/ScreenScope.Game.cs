using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One entry in a screen's Buttons region that is not captured from the window's own draw
    /// (windowless screens, or actions ButtonTextCapture cannot see). Label arrives already
    /// localized; <see cref="ActionId"/>, when set, resolves the player's current (possibly
    /// rebound) chord for the spoken hotkey fragment.
    /// </summary>
    public sealed class ScreenAction
    {
        public readonly string Label;
        public readonly string ActionId;
        public readonly Action Activate;

        /// <summary>
        /// Null (the default) presents this row as a plain <see cref="ElementRole.Button"/>. A
        /// toolbar row mirroring a real vanilla checkbox with no content-region home of its own
        /// sets this, so <see cref="ScreenScope.DescribeActionRow"/> presents it as
        /// <see cref="ElementRole.Checkbox"/> with the live state audible on focus, not only
        /// after toggling.
        /// </summary>
        public readonly CheckState? Check;

        /// <summary>
        /// A declared action the player can focus but not currently run: the row stays navigable
        /// and speaks its disabled state on focus; Enter speaks the standard disabled refusal
        /// (plus <see cref="DisabledReason"/> when supplied) instead of invoking
        /// <see cref="Activate"/>.
        /// </summary>
        public readonly bool Disabled;

        /// <summary>Localized reason fragment spoken with the disabled state; null for none.</summary>
        public readonly string DisabledReason;

        public ScreenAction(string label, Action activate, string actionId = null, CheckState? check = null,
            bool disabled = false, string disabledReason = null)
        {
            Label = label ?? "";
            Activate = activate;
            ActionId = actionId;
            Check = check;
            Disabled = disabled;
            DisabledReason = disabledReason;
        }
    }

    /// <summary>
    /// The standard screen shape, and with <see cref="ScreenModel"/> the home of the screen-model
    /// contract: a screen is an ordered set of REGIONS, each a flat list of items, plus an
    /// automatic final "Buttons" region holding the window's real bottom buttons. Tab/Shift+Tab
    /// cycle regions (one shared rebindable pair, menus.nextRegion/previousRegion);
    /// Up/Down/Home/End move within the current region; Enter activates.
    ///
    /// Behavior policy (wrap, per-region position memory, the tab-switch sound) reads mod-wide
    /// defaults from <see cref="ScreenPolicy"/> through virtual properties, so a per-screen
    /// deviation is one named override. Subclasses MUST NOT hand-roll selection/tab arithmetic;
    /// everything routes through the shared <see cref="ScreenModel"/>.
    ///
    /// An empty CONTENT region still exists for navigation: Tab reaches it, it announces itself
    /// as empty, and the cursor may rest in it — the keys that refill it are often gated on the
    /// cursor being in it, so skipping it would trap the user outside
    /// (<see cref="ContentRegionAlwaysNavigable"/>). Extras, Buttons and opted-out regions keep
    /// the old rule: Tab steps over them while empty, a direct jump is refused, and one that
    /// empties under the cursor relocates it to the nearest region holding something
    /// (<see cref="ReconcileEmptyRegion"/>). Announced positions count only reachable regions.
    ///
    /// The Buttons region: when this scope drives a real window (<see cref="OwnedWindow"/>
    /// non-null) and <see cref="IncludeActionsRegion"/> is true, ButtonTextCapture records every
    /// Widgets.ButtonText the window itself draws (bracketed to its InnerWindowOnGUI by
    /// <see cref="ScreenScopeDrawPatch"/>), and Enter on a captured row injects the click so
    /// vanilla's own inline handler runs unmodified. <see cref="DeclaredActions"/> appends
    /// mod-side actions after the captured buttons. Memorized chords keep working through each
    /// screen's own claims; the Buttons region makes the same commands discoverable and speaks
    /// their chord.
    ///
    /// Subclasses overriding <see cref="OnPush"/>/<see cref="OnPop"/>/<see cref="OnFocus"/> must
    /// call the base implementation — the Buttons-region bracket registration lives there.
    /// </summary>
    public abstract partial class ScreenScope : FocusScope
    {
        protected readonly ScreenModel Model = new ScreenModel();

        private readonly List<RegionSpec> regionSpecs = new List<RegionSpec>();
        private readonly List<ButtonTextCapture.CapturedButton> capturedButtons = new List<ButtonTextCapture.CapturedButton>();

        /// <summary>Raw capture-stream position of each kept button (see <see cref="KeepCapturedButton"/>).</summary>
        private readonly List<int> capturedButtonSourceIndex = new List<int>();

        // Captured-extras region storage; the opt-in gate is in the "captured-extras region"
        // section below.
        private readonly List<CapturedWidget> widgetSnapshot = new List<CapturedWidget>();
        private readonly List<ExtrasRow> extrasRows = new List<ExtrasRow>();
        private readonly List<PointerHitCandidate> routeCandidates = new List<PointerHitCandidate>();
        private readonly List<RouteTarget> routeTargets = new List<RouteTarget>();
        private readonly TextFieldEditSession extrasEditSession = new TextFieldEditSession();
        private int extrasEditingCaptureIndex = -1;
        private ExtrasPendingChange? extrasPendingChange;

        // Empty-region reconciliation (ReconcileEmptyRegion): the guard keeps a subclass's
        // model-refreshing handlers from re-entering the relocation; the flag lets the handler
        // that triggered the refresh stand down so the relocation is the only thing spoken.
        private bool reconcilingEmptyRegion;
        private bool relocatedThisRefresh;

        /// <summary>
        /// Sticky once true: has any region in this model ever held a real item (a table's
        /// header-only row does not count)? Latched in RefreshModel from Model.NonEmptyRegionCount
        /// read BEFORE that pass's rebuild, so it reflects the state going INTO the refresh —
        /// Model.RegionCount would flip true on the first SetRegions call, before boot-time
        /// trickle population has put anything in a region. ReconcileEmptyRegion reads this to
        /// tell "the screen just emptied out" (cue) apart from still-populating (silent). Nothing
        /// rebuilds this Model within one instance's lifetime, so no reset path clears it.
        /// </summary>
        private bool modelEverHadItems;

        // Enter double-press proceed: which (region, item) is armed against DefaultAcceptActionId,
        // or the unarmed sentinel (-1, -1). See OnActivateChord.
        private int armedDefaultAcceptRegion = -1;
        private int armedDefaultAcceptIndex = -1;

        /// <summary>
        /// True once this scope instance's <see cref="RefreshContent"/> has thrown and not yet
        /// succeeded since. While true, <see cref="IsLive"/> reports false so the scope goes
        /// fully inert to the dispatcher instead of sitting on the stack as a live modal that
        /// claims nothing and masks everything.
        /// </summary>
        private bool modelFailed;

        /// <summary>Whether this instance has already spoken the one-time failure announcement — see <see cref="RefreshModel"/>.</summary>
        private bool announcedModelFailure;

        /// <summary>
        /// True from the moment this scope becomes focused (push, or refocus after an overlay
        /// closes) until its very next composed item announcement — the "just arrived" window in
        /// which <see cref="ComposeCurrentText"/> adds the entry region context. Cleared
        /// unconditionally by the first such call, whether or not it added the region frame, so
        /// it can never resurface later if the region count changes mid-session.
        /// </summary>
        private bool awaitingEntryAnnouncement;

        /// <summary>
        /// The heading depth most recently SPOKEN, and the region it was spoken in — the change
        /// gate <see cref="ComposeCurrentText"/> applies so arrowing between siblings says "level
        /// two" once instead of on every row. Null means nothing spoken yet at this depth context,
        /// which makes the next level fragment sound. <see cref="AnnounceRegion"/> advances this
        /// without gating itself, since a Tab landing restates the depth on purpose.
        ///
        /// Must live here, on the one path that actually speaks, and NOT on
        /// <see cref="DescribeContentItem"/> where the depth is filled in: that describe call also
        /// runs from incidental sweeps (the typeahead haystack build, once per visible row) which
        /// would drive the gate off whichever row a loop visited last.
        /// </summary>
        private int? lastAnnouncedLevel;
        private int lastAnnouncedLevelRegion = -1;

        protected ScreenScope()
        {
            // Typeahead claims first: while a search is active they win Escape, Backspace and
            // Shift+Enter ahead of any subclass claim (registration order).
            Claim(SharedMenuGrammar.Cancel, e => TypeaheadEscapeClear(), when: TypeaheadClaimable);
            Claim(SharedMenuGrammar.SearchBackspace, e => TypeaheadBackspace(), when: TypeaheadClaimable);
            Claim(SharedMenuGrammar.SearchSettle, e => TypeaheadSettle(), when: TypeaheadClaimable);

            Claim(SharedMenuGrammar.Next, e => MoveItem(1), when: ItemNavigationClaimable);
            Claim(SharedMenuGrammar.Previous, e => MoveItem(-1), when: ItemNavigationClaimable);
            Claim(SharedMenuGrammar.First, e => MoveItemEdge(true), when: ItemNavigationClaimable);
            Claim(SharedMenuGrammar.Last, e => MoveItemEdge(false), when: ItemNavigationClaimable);
            Claim(SharedMenuGrammar.NextRegion, e => MoveRegion(true), when: RegionCyclingClaimable);
            Claim(SharedMenuGrammar.PreviousRegion, e => MoveRegion(false), when: RegionCyclingClaimable);
            Claim(SharedMenuGrammar.Activate, e => OnActivateChord());
            ClaimFallback(SharedMenuGrammar.ActivateAlias, e => ActivateCurrent(), when: ActivateAliasClaimable);
            // Claimed only when this screen names a proceed button at all (declared id or captured
            // action), so a screen with neither never sees the chord.
            Claim(SharedMenuGrammar.ActivateDefault, e => OnActivateDefaultChord(), when: () => HasDefaultAccept && !ContentOwnsActivateDefault);
            Claim(SharedMenuGrammar.NextHorizontal, e => OnHorizontal(1), when: HorizontalClaimable);
            Claim(SharedMenuGrammar.PreviousHorizontal, e => OnHorizontal(-1), when: HorizontalClaimable);
            Claim(SharedMenuGrammar.SortColumn, e => ToggleSortCurrentColumn(), when: SortChordClaimable);
            // Unconditional: a screen that cannot match the pointer answers with the standard
            // refusal, which beats the modal swallow eating the chord silently.
            Claim(SharedMenuGrammar.RouteToPointer, e => RouteToPointer());
            // Unconditional too: a settings flip is valid whatever the screen shows.
            Claim(SharedMenuGrammar.ToggleMouseTracking, e => PointerWarpState.ToggleMouseTracking());
            Claim(SharedMenuGrammar.ReorderUp, e => ReorderCurrent(-1), when: () => ReorderClaimable(-1));
            Claim(SharedMenuGrammar.ReorderDown, e => ReorderCurrent(1), when: () => ReorderClaimable(1));

            // Chassis safety net for real windows: unclaimed Escape runs the attached window's
            // own cancel body, unclaimed Shift+Enter its accept where vanilla opts in. Without
            // these the modal swallow eats the KeyDown before the window's own key tests see it
            // (those run pre-dispatcher only while the window holds IMGUI focus).
            ClaimFallback(SharedMenuGrammar.Cancel, e => CancelAttachedWindow(), when: CanCancelAttachedWindow);
            ClaimFallback(SharedMenuGrammar.ActivateDefault, e => AcceptAttachedWindow(), when: CanAcceptAttachedWindow);

            // '?' opens the Learning Helper from any screen (even when this scope is the topmost
            // modal); Escape dismisses it. See LearningHelperOpenerClaims.
            LearningHelperOpenerClaims.RegisterForScreen(this);

            RegisterPopTeardown(extrasEditSession.CancelIfActive);
        }

        /// <summary>
        /// False while <see cref="modelFailed"/>. <see cref="FocusStackCore.Dispatch"/> skips a
        /// non-live scope entirely (no claim, no modal-mask "stop the walk" branch) and
        /// <see cref="FocusStackCore.AnyLiveInputOwner"/> — which <c>MenuOwnsInput</c> is built
        /// from — also gates on <c>IsLive</c>, so a failed scope stops masking AND stops causing
        /// the native modal swallow with no second gate anywhere else. Vanilla's Escape (via
        /// <see cref="WindowKeyRouter"/>, which also checks <c>top.IsLive</c> before consulting
        /// <c>OwnsCancel</c>) then closes the window and the window-watch pops this scope.
        /// </summary>
        public override bool IsLive
        {
            get { return !modelFailed; }
        }

        /// <summary>
        /// Enter belongs to this scope's Activate claim (which stamps the accept frame); vanilla's
        /// OnAcceptKeyPressed stays blocked so one Enter can't both activate a row and accept the
        /// dialog.
        /// </summary>
        public override bool OwnsAccept
        {
            get { return true; }
        }

        /// <summary>
        /// Escape keeps vanilla's meaning — EXCEPT while a typeahead search is active, when it
        /// clears the search instead. The constructor's fallback claim drives the attached
        /// window's own cancel body itself. Subclasses that must handle Escape override this
        /// (folding in <c>base.OwnsCancel</c>) and claim menus.cancel; the fallback stands down.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return TypeaheadHasActiveSearch; }
        }

        private bool CanCancelAttachedWindow()
        {
            if (OwnsCancel)
            {
                return false;
            }
            Window window = ScopeForWindow.WindowOf(this);
            return window != null && window.closeOnCancel;
        }

        private void CancelAttachedWindow()
        {
            Window window = ScopeForWindow.WindowOf(this);
            if (window == null)
            {
                return;
            }
            // The window's own cancel body (a Close, unless the dialog overrides it), then the
            // stamp so vanilla's independent same-frame re-tests cannot close a second window.
            DeliberateWindowKeyCall.Run(window.OnCancelKeyPressed);
            ShellFrameStamps.MarkCancelConsumed();
        }

        // closeOnAccept false means accept has a bespoke vehicle (or none), never a plain close.
        private bool CanAcceptAttachedWindow()
        {
            if (HasDefaultAccept)
            {
                return false;
            }
            Window window = ScopeForWindow.WindowOf(this);
            return window != null && window.closeOnAccept;
        }

        private void AcceptAttachedWindow()
        {
            Window window = ScopeForWindow.WindowOf(this);
            if (window == null)
            {
                return;
            }
            DeliberateWindowKeyCall.Run(window.OnAcceptKeyPressed);
            ShellFrameStamps.MarkAcceptConsumed();
        }

        // ------------------------------------------------------------------
        // The subclass contract: regions and items.
        // ------------------------------------------------------------------

        /// <summary>Number of content regions (excluding the automatic Buttons region).</summary>
        protected abstract int ContentRegionCount { get; }

        /// <summary>Localized name of a content region ("Colonists", "Items", "Info").</summary>
        protected abstract string ContentRegionName(int region);

        /// <summary>Item count of a content region, read fresh on every refresh.</summary>
        protected abstract int ContentItemCount(int region);

        /// <summary>Describe one content item for speech; the base fills in position fragments left unset.</summary>
        protected abstract ElementDescription DescribeContentItem(int region, int index);

        /// <summary>Enter on a content item.</summary>
        protected abstract void ActivateContentItem(int region, int index);

        /// <summary>
        /// Rebuild whatever row data the subclass caches; called before every count/describe cycle
        /// so IMGUI-fresh game state and the model never drift apart.
        /// </summary>
        protected virtual void RefreshContent()
        {
        }

        /// <summary>Left/Right adjust for the current content item (sliders, steppers). Pair with <see cref="CanAdjustContentItem"/>.</summary>
        protected virtual void AdjustContentItem(int region, int index, int direction)
        {
        }

        /// <summary>Gate for the shared Left/Right claims; false (the default) lets the chords fall through.</summary>
        protected virtual bool CanAdjustContentItem(int region, int index)
        {
            return false;
        }

        /// <summary>
        /// Whether the focused content item is itself a navigable surface answering Up/Down/Home/End
        /// with its own meaning instead of moving the region's row cursor. False by default.
        ///
        /// For an element that is a WHOLE surface rather than a row (a world map, where the arrows
        /// are a compass): host it as a one-item region, return true for that item, and claim the
        /// element's chords in the constructor — the base's claims stand down while the cursor is on
        /// the element, so the subclass's later-registered claims are reached, and Tab into any other
        /// region takes them back with no further bookkeeping. Regions this scope owns itself
        /// (Buttons, captured extras) and table regions are never affected.
        ///
        /// Does NOT suppress Left/Right (<see cref="HorizontalClaimable"/> already declines those for
        /// a flat, non-adjustable row) nor Tab/Enter, which keep their screen-wide meaning.
        /// </summary>
        protected virtual bool ContentItemOwnsNavigationKeys(int region, int index)
        {
            return false;
        }

        /// <summary>
        /// Whether a content region stays reachable by Tab while empty. True (the default) keeps
        /// it in the tab cycle, announcing itself as empty: the keys that refill an empty section
        /// (a column switch, a tab strip, a pane selection) are often gated on the cursor being
        /// IN it, so skipping it would trap the user outside with no way back.
        ///
        /// Return false ONLY for a region the sighted player cannot currently see — a mode where
        /// the game does not draw it at all. Consulted for content regions only (Buttons and
        /// captured-extras keep the plain empty-region rule); nothing sends the cursor in on its
        /// own — only an explicit Tab or direct jump lands here while empty.
        /// </summary>
        protected virtual bool ContentRegionAlwaysNavigable(int region)
        {
            return true;
        }

        /// <summary>
        /// Gate for the shared Ctrl+Up/Down reorder claims — the keyboard equivalent of a
        /// drag-and-drop. False (the default) lets the chords fall through, so a screen that gives
        /// Ctrl+Arrow another meaning is never shadowed. <paramref name="direction"/> is -1 (up) or
        /// +1 (down); an item already first/last reports false for that direction alone.
        /// </summary>
        protected virtual bool CanReorderContentItem(int region, int index, int direction)
        {
            return false;
        }

        /// <summary>
        /// Perform the requested reorder on the screen's own vanilla mutation vehicle (the same list
        /// method or delegate a mouse drag would call), keeping the backing list consistent with what
        /// <see cref="ContentItemCount"/>/<see cref="DescribeContentItem"/> report next refresh.
        /// Returns the item's NEW index within the region so the cursor can follow it, or -1 when a
        /// vanilla-side veto refused the move at mutation time.
        /// </summary>
        protected virtual int ReorderContentItem(int region, int index, int direction)
        {
            return -1;
        }

        // ------------------------------------------------------------------
        // The table contract: a content region becomes a TABLE by reporting a positive column
        // count. Rows keep the list contract — ContentItemCount is the DATA row count and
        // DescribeContentItem(region, row) is the row's identity (its label column) — while the
        // base adds the header row (row 1 in speech, model index 0) and all offset arithmetic. In
        // a table region Left/Right move the column cursor, Enter on the header row cycles the
        // sort, and Enter on a data cell tries the cell first, then ActivateContentItem.
        // ------------------------------------------------------------------

        /// <summary>Column count of a content region; 0 (the default) keeps it a flat list.</summary>
        protected virtual int ContentColumnCount(int region)
        {
            return 0;
        }

        /// <summary>One column's header label, tooltip, and sortability (localized).</summary>
        protected virtual TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return null;
        }

        /// <summary>One cell's value for speech, already formatted/localized. Row is the DATA row (0-based, header excluded).</summary>
        protected virtual string ContentCellText(int region, int row, int column)
        {
            return null;
        }

        /// <summary>One cell's own tooltip, or null. Spoken as the verbose tail.</summary>
        protected virtual string ContentCellTip(int region, int row, int column)
        {
            return null;
        }

        /// <summary>A radio cell's chosen state, or null for a cell with none.</summary>
        protected virtual bool? ContentCellSelected(int region, int row, int column)
        {
            return null;
        }

        /// <summary>A data row's own tooltip, or null. Spoken only when the row changes.</summary>
        protected virtual string ContentRowTip(int region, int row)
        {
            return null;
        }

        /// <summary>
        /// Enter on a data cell. True when the cell itself handled it (an interactive column);
        /// false falls back to the row's default action (<see cref="ActivateContentItem"/>).
        /// </summary>
        protected virtual bool ActivateContentCell(int region, int row, int column)
        {
            return false;
        }

        /// <summary>
        /// Re-order the region's data for a new sort state, using the game's own comparers (never
        /// display strings). <paramref name="currentRow"/> is the DATA row the cursor was on (-1 on
        /// the header row); return its new data index so the selection survives, or -1 to leave the
        /// cursor where it is.
        /// </summary>
        protected virtual int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            return -1;
        }

        /// <summary>
        /// The shared Alt+S sort-by-current-column chord. Screens whose Alt+S means something else
        /// (a legacy send/accept chord) override false; Enter on the header row still sorts.
        /// </summary>
        protected virtual bool EnableSortChord
        {
            get { return true; }
        }

        // Policy: ScreenPolicy defaults; a deviating screen overrides ONE of these instead of
        // copying cursor math.

        public virtual bool WrapTabs
        {
            get { return ScreenPolicy.WrapTabs; }
        }

        public virtual bool RememberTabPositions
        {
            get { return ScreenPolicy.RememberTabPositions; }
        }

        protected virtual bool WrapItems
        {
            get { return ScreenPolicy.WrapItems; }
        }

        protected virtual SoundDef TabSwitchSound
        {
            get { return ScreenPolicy.TabSwitchSound; }
        }

        // The Buttons region.

        /// <summary>Append the automatic Buttons region (captured and/or declared actions).</summary>
        protected virtual bool IncludeActionsRegion
        {
            get { return true; }
        }

        /// <summary>
        /// Capture the window's own <c>Widgets.ButtonText</c> calls into the Buttons region.
        /// Correct for button-only dialogs, where every text button IS an action. A screen whose
        /// CONTENT also draws buttons (sort headers, per-row buttons) over-captures and must set
        /// this false and enumerate its real actions in <see cref="DeclaredActions"/> instead.
        /// </summary>
        protected virtual bool CaptureWindowButtons
        {
            get { return true; }
        }

        /// <summary>Localized name of the Buttons region.</summary>
        protected virtual string ActionsRegionName
        {
            get { return "RimWorldAccess.Shell.Screen.ActionsRegion".Translate().ToString(); }
        }

        /// <summary>Mod-side actions appended after the window's captured buttons; null/empty for none.</summary>
        protected virtual IReadOnlyList<ScreenAction> DeclaredActions
        {
            get { return null; }
        }

        /// <summary><see cref="DeclaredActions"/> plus the <see cref="CompatScreenActions"/> rows
        /// registered for this scope's Name — the one list every Buttons-region consumer reads,
        /// so a compat module's row is navigated and activated exactly like a scope's own.</summary>
        private IReadOnlyList<ScreenAction> ResolvedDeclaredActions()
        {
            IReadOnlyList<ScreenAction> declared = DeclaredActions;
            List<ScreenAction> compat = CompatScreenActions.CollectFor(Name);
            if (compat == null)
            {
                return declared;
            }
            if (declared == null || declared.Count == 0)
            {
                return compat;
            }
            var merged = new List<ScreenAction>(declared.Count + compat.Count);
            merged.AddRange(declared);
            merged.AddRange(compat);
            return merged;
        }

        /// <summary>
        /// Spoken hotkey fragment for a captured button, by capture index; screens that also expose
        /// the command as a chord map it here so the announcement teaches the shortcut.
        /// </summary>
        protected virtual string CapturedButtonHotkey(int captureIndex)
        {
            return null;
        }

        /// <summary>
        /// Whether a captured button belongs in the Buttons region. A screen whose CONTENT rows
        /// already model one specific window button drops it here so it is not presented twice,
        /// comparing against the LIVE label the game composes, never hardcoded text. Dropped
        /// buttons stay drawn and clickable; only this scope's row model skips them.
        /// </summary>
        protected virtual bool KeepCapturedButton(string rawLabel)
        {
            return true;
        }

        /// <summary>
        /// Spoken label for a captured button; default is the button's own captured text. A scope
        /// whose window draws a button whose label alone cannot carry its meaning overrides this to
        /// name the function — naming in the mod's own terms, never commentary.
        /// </summary>
        protected virtual string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            return rawLabel;
        }

        /// <summary>
        /// The window the pointer must be resting on for Alt+Shift+J to route from. Defaults to the
        /// scope's own window; a scope driving a real window it was never ATTACHED to names that
        /// window here so routing can consult its geometry.
        /// </summary>
        protected virtual Window PointerSurface
        {
            get { return OwnedWindow; }
        }

        /// <summary>
        /// The real window this scope drives, or null for windowless screens, which get no captured
        /// buttons: their Buttons region is <see cref="DeclaredActions"/> alone.
        /// </summary>
        protected internal virtual Window OwnedWindow
        {
            get { return ScopeForWindow.WindowOf(this); }
        }

        /// <summary>The player's current chord for an action id, spoken-form ("Alt+S"), or null.</summary>
        protected static string ChordDisplay(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return null;
            InputAction action;
            if (!ActionRegistry.Catalog.TryGet(actionId, out action) || action.Bindings.Count == 0)
                return null;
            return action.Bindings[0].DisplayLabel;
        }

        // The captured-extras region: an opt-in net surfacing every widget the owned window's draw
        // records (via WidgetCapture) that this scope's typed content/actions regions do not
        // already present, so a mod injecting a control onto this page is hearable and operable
        // with no bespoke work. Off by default; a scope opts in only once its own content is
        // stable enough that "not already presented" is a meaningful filter.

        /// <summary>Append the automatic "Additional controls" region for widgets this scope does not already present.</summary>
        protected virtual bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        /// <summary>Also keep unmirrored text-only captured rows as read-only extras rows, for a
        /// scope whose typed regions cannot model the window's informational text.</summary>
        protected virtual bool ExtrasIncludeReadOnlyRows
        {
            get { return false; }
        }

        /// <summary>
        /// Extra strings this scope presents by means the captured-extras diff cannot see (a value
        /// folded into a content item's Extras/Hint, a declared action whose label differs from the
        /// captured button text), folded into the diff's presented set so the corresponding widget
        /// is not ALSO offered as an extra. Null (the default) adds nothing.
        /// </summary>
        protected virtual IEnumerable<string> AdditionalPresentedTexts
        {
            get { return null; }
        }

        /// <summary>Bracket gate: only a scope that opted in AND owns a real window arms the widget-capture pass.</summary>
        internal bool WantsWidgetCapture
        {
            get { return IncludeCapturedExtrasRegion && OwnedWindow != null; }
        }

        /// <summary>Localized name of the captured-extras region.</summary>
        protected virtual string ExtrasRegionName
        {
            get { return "RimWorldAccess.Shell.Screen.ExtrasRegion".Translate().ToString(); }
        }

        // Lifecycle.

        public override void OnPush()
        {
            ScreenScopeDrawPatch.Register(this);
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.Unregister(this);
            if (extrasEditingCaptureIndex >= 0)
            {
                WidgetCapture.ClearTextOverride();
                extrasEditingCaptureIndex = -1;
            }
            suppressNextEntryAnnouncement = false;
        }

        public override void OnFocus()
        {
            DisarmDefaultAccept();
            awaitingEntryAnnouncement = !suppressNextEntryAnnouncement;
            suppressNextEntryAnnouncement = false;
            lastAnnouncedLevel = null;
            lastAnnouncedLevelRegion = -1;
            buttonPassSinceFocus = false;
            widgetPassSinceFocus = false;
            RefreshModel();
            if (!openAnnouncementAttempted)
            {
                openAnnouncementAttempted = true;
                string open = ComposeOpenAnnouncement();
                if (!string.IsNullOrEmpty(open))
                {
                    TolkHelper.SpeakData(open);
                }
            }
        }

        private bool openAnnouncementAttempted;
        private bool suppressNextEntryAnnouncement;

        /// <summary>
        /// One-shot: the screen's own state machinery owns the upcoming open's announcement, so the
        /// NEXT focus must not arm the automatic entry announcement. Consumed by the next OnFocus;
        /// cleared on pop. Without it a state-side open announcement and the entry announcement
        /// double up, masked only when both land in the same frame with identical text.
        /// </summary>
        protected void SuppressNextEntryAnnouncement()
        {
            suppressNextEntryAnnouncement = true;
        }

        /// <summary>
        /// One-shot open announcement, composed live and spoken at the end of the base OnFocus:
        /// after the model refresh, before the entry-item announcement. Attempted once per scope
        /// lifetime, so a null first compose stays silent forever. Null (the default) = none.
        /// </summary>
        protected virtual string ComposeOpenAnnouncement()
        {
            return null;
        }

        /// <summary>
        /// Re-arms <see cref="ComposeOpenAnnouncement"/> for the next focus; call from
        /// <c>OnPush</c>. Only a scope whose instance OUTLIVES one opening of its screen (a
        /// singleton behind a mirror) needs it, else the announcement is spoken once per session
        /// rather than once per open.
        /// </summary>
        protected void ResetOpenAnnouncement()
        {
            openAnnouncementAttempted = false;
        }

        /// <summary>
        /// Whether each capture pass has completed at least once since the last focus. The entry
        /// announcement includes a region frame ("tab, i of N"), and N is a LIE until every
        /// capture-backed region this scope wants has had one pass to populate, so
        /// <see cref="TryAnnounceEntryIfPending"/> holds the announcement until the wanted passes
        /// land (one frame), keeping the spoken count identical to what Tab reports.
        /// </summary>
        private bool buttonPassSinceFocus;
        private bool widgetPassSinceFocus;

        /// <summary>
        /// Speaks the freshly focused item once the model actually has content. Fires strictly
        /// after this scope's own <see cref="OnFocus"/> AND any subclass override body have
        /// finished, so a subclass speaking its own title synchronously in <c>OnFocus</c> is never
        /// preempted. When the model is still empty here (a region that populates from a LATER
        /// capture pass), <see cref="awaitingEntryAnnouncement"/> stays armed and
        /// <see cref="OnButtonPassCompleted"/>/<see cref="OnWidgetPassCompleted"/> re-check once
        /// that pass lands.
        /// </summary>
        public override void AfterFocusDispatch()
        {
            TryAnnounceEntryIfPending();
        }

        /// <summary>
        /// Speaks the entry announcement exactly once per focus, the moment the model has real
        /// content. Idempotent by construction: <see cref="awaitingEntryAnnouncement"/> is consumed
        /// by whichever announce call reaches <see cref="ComposeCurrentText"/> first, so calling
        /// this from several places can never double up. Also skips a refresh that already spoke
        /// for itself (<see cref="relocatedThisRefresh"/>).
        /// </summary>
        private void TryAnnounceEntryIfPending()
        {
            if (!awaitingEntryAnnouncement || relocatedThisRefresh)
            {
                return;
            }
            if (WantsButtonCapture && !buttonPassSinceFocus)
            {
                return;
            }
            if (WantsWidgetCapture && !widgetPassSinceFocus)
            {
                return;
            }
            // Items, not navigable regions: an always-navigable region is in the cycle while
            // empty, and a screen still populating must not open with "empty".
            if (Model.CurrentRegion == null || !Model.AnyRegionHasItems)
            {
                return;
            }
            AnnounceCurrentItem();
        }

    }
}
