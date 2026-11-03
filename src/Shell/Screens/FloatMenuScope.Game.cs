using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A real, visible <see cref="FloatMenu"/> (or FloatMenuMap/FloatMenuWorld) driven by keyboard,
    /// attached through the ScopeForWindow mirror whenever one of those exact types enters the
    /// WindowStack — menus our own flows construct and menus vanilla spawns for mouse users alike.
    /// Mouse behavior is untouched: hover, click, click-outside and vanish-on-mouse-distant all keep
    /// working, and any close path pops the scope via the mirror.
    ///
    /// One content region — the menu's own priority-sorted option list — so navigation, typeahead
    /// and announcement composition come from the chassis. No Buttons region: a float menu draws no
    /// button row. Rects come from FloatMenuOptionCapture during the menu's real draw, so the focus
    /// ring and auto-scroll agree with vanilla's layout in every column and scroll case. Activation
    /// runs <see cref="FloatMenuOption.Chosen"/> against the real menu, so vanilla's colonist-order
    /// sound and FloatMenuMap's PreOptionChosen revalidation both apply, then removes the menu as
    /// vanilla's click handler does. Keyboard deviation: activating a DISABLED option keeps the menu
    /// open with a reject sound instead of closing it.
    ///
    /// THE ACCEPT RULE: FloatMenu inherits closeOnAccept=true and vanilla's deferred re-test would
    /// close the menu behind the scope's back, so Enter belongs to this scope alone, which is what
    /// <see cref="ScreenScope.OwnsAccept"/> answers. Escape stays scope-owned too
    /// (<see cref="OwnsCancel"/>): vanilla's own Cancel path plus the "menu closed" announcement.
    /// </summary>
    public sealed class FloatMenuScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<FloatMenu, List<FloatMenuOption>> optionsField =
            AccessTools.FieldRefAccess<FloatMenu, List<FloatMenuOption>>("options");
        private static readonly AccessTools.FieldRef<FloatMenu, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<FloatMenu, Vector2>("scrollPosition");

        private readonly FloatMenu menu;
        private List<object> savedSelection;
        private bool pendingAnnounce;

        public FloatMenuScope(FloatMenu menu)
        {
            this.menu = menu;
            // Shift+Enter: a float menu has no proceed button, so the chord keeps its vanilla
            // shift-click meaning and queues the order on colonist-order menus. Claimed after the
            // base, whose SearchSettle claim shares the chord during a search.
            Claim(SharedMenuGrammar.ActivateDefault, e => ActivateCurrent(),
                when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
        }

        public override string Name
        {
            get { return "float-menu"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>
        /// Escape is this scope's throughout: the claim runs vanilla's own cancel path and announces
        /// the close, so the menu must never also close itself out from under that. During a search
        /// the base's claim clears it first, by registration order.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>A float menu has no button row of its own to capture.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, menu);
        }

        internal bool IsFocusedOption(FloatMenuOption option)
        {
            int index = FocusedIndex;
            List<FloatMenuOption> options = Options;
            return index >= 0 && index < options.Count && ReferenceEquals(options[index], option);
        }

        private List<FloatMenuOption> Options
        {
            get { return optionsField(menu) ?? new List<FloatMenuOption>(); }
        }

        /// <summary>
        /// The option row the cursor rests on, read straight off the model without refreshing it:
        /// the draw-pass callers must not mutate scope state mid-pass.
        /// </summary>
        private int FocusedIndex
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                return region == null ? -1 : region.Index;
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            TooltipCapture.Arm();
            // Some option actions expect the objects selected at open time to still be selected when
            // they run. Find.Selector throws during chargen, hence the map guard.
            savedSelection = Find.CurrentMap != null && Find.Selector != null
                ? new List<object>(Find.Selector.SelectedObjects)
                : null;
        }

        public override void OnPop()
        {
            base.OnPop();
            TooltipCapture.Disarm();
        }

        public override void OnFocus()
        {
            // The first announcement waits for the menu's own draw pass: an option's tooltip
            // resolves from the rect captured there, so announcing earlier drops the tip.
            SuppressNextEntryAnnouncement();
            base.OnFocus();
            pendingAnnounce = true;
        }

        // Content: the menu's own option list.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses the shared "Menu" phrase; a single-region screen only ever speaks it on a region frame.</summary>
        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.MainMenu.MenuRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return Options.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<FloatMenuOption> options = Options;
            if (index < 0 || index >= options.Count)
            {
                return new ElementDescription();
            }
            FloatMenuOption option = options[index];
            var d = new ElementDescription();
            d.Label = option.Label;
            d.Hotkey = KeyboardFloatMenu.HotkeyFor(option);
            d.Role = ElementRole.MenuItem;
            d.Disabled = option.Disabled;
            // FloatMenuOption.DoGUI registers its tip unconditionally, so the capture sees it every
            // pass.
            Rect rect;
            if (FloatMenuOptionCapture.TryGetRect(option, out rect))
            {
                d.Extras = TooltipCapture.TryResolveAt(rect);
            }
            return d;
        }

        /// <summary>
        /// The bare option label, not the composed description: the haystack is rebuilt per row per
        /// keystroke, and Extras resolves through the tooltip index, which matching does not need.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            List<FloatMenuOption> options = Options;
            return row >= 0 && row < options.Count ? (options[row].Label ?? "") : "";
        }

        protected override void ActivateContentItem(int region, int index)
        {
            List<FloatMenuOption> options = Options;
            if (index < 0 || index >= options.Count)
            {
                return;
            }
            FloatMenuOption option = options[index];

            if (option.Disabled)
            {
                // Deviation from vanilla's close-on-click: keep the menu open to pick something else.
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.LabelUnavailable".Loc(option.Label));
                return;
            }

            // The live shift bit: Shift+Enter queues the order.
            bool queueing = Event.current != null && Event.current.shift && menu.givesColonistOrders;

            // A world "Jump to..." option moves the cursor during its action, so the origin tile is
            // captured to announce the move afterwards.
            PlanetTile worldJumpOrigin = WorldNavigationState.IsActive
                ? WorldNavigationState.CurrentSelectedTile
                : PlanetTile.Invalid;

            RestoreSavedSelection();

            // The vanilla click path: PreOptionChosen revalidation, the colonist-order sound, the
            // action, then the menu leaves the stack, which pops this scope.
            PendingTargetingContext.Set(option.Label);
            try
            {
                option.Chosen(menu.givesColonistOrders, menu);
            }
            finally
            {
                PendingTargetingContext.Clear();
            }
            Find.WindowStack.TryRemove(menu);

            // Revalidation may have disabled the option at choose time, in which case the action did
            // not run and success must not be claimed.
            if (option.Disabled)
            {
                TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.LabelUnavailable".Loc(option.Label));
                return;
            }

            // Placement mode, a fresh targeting session (vanilla's or a mod's map targeter), and an
            // announced world jump all speak for themselves.
            bool targetingStarted = Find.CurrentMap != null && ExternalMapTargeting.MapTargetingActive;
            bool worldJumpAnnounced = WorldNavigationState.FollowExternalJumpAndAnnounce(worldJumpOrigin);
            if (!ArchitectState.IsInPlacementMode && !targetingStarted && !worldJumpAnnounced)
            {
                if (queueing)
                {
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.SelectedQueued".Loc(option.Label, "Queued".Translate()));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.Selected".Loc(option.Label));
                }
            }
        }

        // Per-GUI-pass work, driven by the menu's own draw.

        internal void OnGuiPass(Rect inRect)
        {
            List<FloatMenuOption> options = Options;
            RefreshModel();
            AutoScrollToFocused(options, inRect);

            if (pendingAnnounce && options.Count > 0 && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                pendingAnnounce = false;
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Keeps the focused option visible: vanilla's scroll view is mouse-wheel-only, so the
        /// keyboard cursor nudges the position directly. Captured rects are view-space, and the
        /// visible band runs from scrollPosition.y to that plus inRect.height.
        /// </summary>
        private void AutoScrollToFocused(List<FloatMenuOption> options, Rect inRect)
        {
            int index = FocusedIndex;
            if (index < 0 || index >= options.Count)
            {
                return;
            }
            Rect rect;
            if (!FloatMenuOptionCapture.TryGetRect(options[index], out rect))
            {
                return;
            }
            ref Vector2 scroll = ref scrollPositionField(menu);
            if (rect.y < scroll.y)
            {
                scroll.y = rect.y;
            }
            else if (rect.yMax > scroll.y + inRect.height)
            {
                scroll.y = rect.yMax - inRect.height;
            }
        }

        private void RestoreSavedSelection()
        {
            if (savedSelection == null || Find.CurrentMap == null || Find.Selector == null)
            {
                return;
            }
            Find.Selector.ClearSelection();
            for (int i = 0; i < savedSelection.Count; i++)
            {
                ISelectable selectable = savedSelection[i] as ISelectable;
                if (selectable != null)
                {
                    Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);
                }
            }
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            // Vanilla's own cancel path: FloatMenu_Cancel sound + TryRemove.
            menu.Cancel();
            TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
        }

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            List<FloatMenuOption> options = Options;
            int index = FocusedIndex;
            if (index < 0 || index >= options.Count)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            InfoCardState.TryOpenInfoCardForDef(ResolveInfoDef(options[index]));
        }

        /// <summary>
        /// The focused option's info-card subject, by precedence: the icon item, the icon thing, then
        /// the click target the option revalidates against. Shared with the compat scopes driving a
        /// mod's own window of FloatMenuOptions.
        /// </summary>
        internal static Def ResolveInfoDef(FloatMenuOption option)
        {
            ThingDef shownItem = AccessTools.Field(typeof(FloatMenuOption), "shownItem").GetValue(option) as ThingDef;
            if (shownItem != null)
            {
                return shownItem;
            }
            if (option.iconThing != null)
            {
                return option.iconThing.def;
            }
            if (option.revalidateClickTarget != null)
            {
                return option.revalidateClickTarget.def;
            }
            return null;
        }
    }

    /// <summary>
    /// Brackets the option capture to the menu's own draw and lets the scope refresh, auto-scroll
    /// and fire deferred announcements. Patches the DECLARING FloatMenu.DoWindowContents:
    /// FloatMenuMap/FloatMenuWorld override it but call base, so this fires for all three after
    /// their revalidation, keeping captured Disabled states fresh.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenu), "DoWindowContents")]
    public static class FloatMenuDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(FloatMenu __instance, out FloatMenuScope __state)
        {
            __state = null;
            try
            {
                FloatMenuScope scope = FocusStack.Top as FloatMenuScope;
                if (scope != null && scope.Owns(__instance))
                {
                    __state = scope;
                    FloatMenuOptionCapture.BeginPass();
                    TooltipCapture.BeginPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Float menu draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(Rect rect, Exception __exception, FloatMenuScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, rect, ReferenceEquals(FocusStack.Top, __state), __exception,
                    () => { FloatMenuOptionCapture.EndPass(); TooltipCapture.EndPass(); }, (s, r) => s.OnGuiPass(r), "Float menu draw pass error");
            }
        }
    }

    /// <summary>
    /// Keyboard entry points that construct real float menus, positioned at the map cursor rather
    /// than the OS mouse and with vanish-on-mouse-distant disabled — the physical mouse can be
    /// anywhere, and the menu would otherwise self-dismiss on the first frame. Mouse-spawned menus
    /// keep both vanilla behaviors.
    /// </summary>
    public static class KeyboardFloatMenu
    {
        /// <summary>
        /// The chord each option of the open menu announces, for openers whose rows stand in for
        /// something the player can also reach by key. FloatMenuOption carries no hotkey channel, so
        /// the pairing rides alongside the menu and is replaced whenever a new one opens.
        /// </summary>
        private static Dictionary<FloatMenuOption, string> hotkeys;

        /// <summary>The chord to announce for this option, or null when it has none.</summary>
        internal static string HotkeyFor(FloatMenuOption option)
        {
            string chord;
            return hotkeys != null && option != null && hotkeys.TryGetValue(option, out chord) ? chord : null;
        }

        /// <summary>Opens a real FloatMenu anchored to the given map cell, or to the mouse when none is given.</summary>
        public static FloatMenu Open(
            List<FloatMenuOption> options,
            bool givesColonistOrders,
            IntVec3? anchorCell = null,
            Dictionary<FloatMenuOption, string> optionHotkeys = null)
        {
            hotkeys = optionHotkeys;
            FloatMenu floatMenu = new FloatMenu(options);
            floatMenu.givesColonistOrders = givesColonistOrders;
            floatMenu.vanishIfMouseDistant = false;
            Find.WindowStack.Add(floatMenu);
            if (anchorCell.HasValue)
            {
                AnchorToCell(floatMenu, anchorCell.Value);
            }
            return floatMenu;
        }

        /// <summary>Re-anchors the menu's window rect to a map cell's screen position, clamped with vanilla's own margin rules.</summary>
        private static void AnchorToCell(FloatMenu floatMenu, IntVec3 cell)
        {
            Vector2 anchor = cell.ToVector3Shifted().MapToUIPosition();
            Rect windowRect = floatMenu.windowRect;
            windowRect.x = Mathf.Clamp(anchor.x, 0f, UI.screenWidth - windowRect.width);
            windowRect.y = Mathf.Clamp(anchor.y, 0f, UI.screenHeight - windowRect.height);
            floatMenu.windowRect = windowRect;
        }
    }
}
