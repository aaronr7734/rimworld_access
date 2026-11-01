using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The in-game pause menu: the REAL MainTabWindow_Menu, opened for real and
    /// driven by keyboard. The scope attaches through ScopeForWindow when the tab
    /// window enters the WindowStack (Escape rung, the bottom-bar Menu button, or
    /// any mod), and pops when it leaves by any path — including vanilla's own
    /// click-outside close in MainTabsRoot.HandleLowPriorityShortcuts.
    ///
    /// One content region, from OptionListingCapture: the labels, actions and rects vanilla actually
    /// drew, plus the mod's own items injected into those same lists so vanilla renders them as
    /// ordinary buttons and keyboard Enter runs the identical action delegates.
    ///
    /// <see cref="IncludeActionsRegion"/> is off: every text button this window draws is already one
    /// of the captured options, so a Buttons region would present the same rows twice.
    ///
    /// <see cref="OwnsAccept"/> keeps the chassis default: the menu ships with closeOnAccept=true and
    /// Window.InnerWindowOnGUI re-tests Accept in the deferred GUI pass where the dispatcher's
    /// Event.Use() is invisible, so an unconsumed Enter would silently close the menu one phase
    /// later. <see cref="OwnsCancel"/> is unconditionally true because this scope claims Escape in
    /// every state.
    ///
    /// Cursor memory: reopening lands on the last activated item within one game
    /// session; an explicit Escape forgets it.
    /// </summary>
    public sealed class PauseMenuScope : ScreenScope
    {
        // Cursor memory across open/close, scoped to one Game session.
        private static string rememberedLabel;
        private static System.WeakReference<Game> rememberedSession;

        // The scope attached to the open menu window. MainTabsRoot allows one open tab at a time,
        // so one slot suffices; it exists for the IsAttached probe.
        private static PauseMenuScope active;

        public static bool IsAttached
        {
            get { return active != null; }
        }

        private sealed class MenuEntry
        {
            public readonly string Label;
            public readonly Action Activate;
            public readonly Rect GuiRect;
            public readonly Rect ScreenRect;

            public MenuEntry(string label, Action activate, Rect guiRect, Rect screenRect)
            {
                Label = label ?? "";
                Activate = activate;
                GuiRect = guiRect;
                ScreenRect = screenRect;
            }
        }

        private readonly List<MenuEntry> entries = new List<MenuEntry>();
        private Rect lastInRect;
        private bool pendingAnnounce;
        private bool cursorRestorePending = true;

        public override string Name
        {
            get { return "pause-menu"; }
        }

        /// <summary>See the class remarks: Escape is claimed in every state.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        public PauseMenuScope()
        {
            // Registered after the base ctor's search-clear Cancel claim, so only an idle Escape
            // closes the menu.
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: () => !TypeaheadHasActiveSearch);
        }

        public override void OnPush()
        {
            base.OnPush();
            active = this;
            OptionListingCapture.Arm(BuildMainColumnExtras(), BuildLinksColumnExtras());
            TooltipCapture.Arm();
        }

        public override void OnPop()
        {
            if (active == this)
            {
                active = null;
            }
            OptionListingCapture.Disarm();
            TooltipCapture.Disarm();
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // The options exist only once vanilla has drawn them, so the entry announcement waits
            // for the menu's own GUI pass.
            pendingAnnounce = true;
        }

        // Flow helpers for the rest of the mod.

        /// <summary>Opens the real pause menu tab (the vanilla Escape menu).</summary>
        public static void OpenRealMenu()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Menu);
        }

        /// <summary>Closes the menu tab if it is open; safe to call any time.</summary>
        public static void CloseRealMenuIfOpen(bool playSound = false)
        {
            MainTabsRoot tabs = Find.MainTabsRoot;
            if (tabs != null && tabs.OpenTab == MainButtonDefOf.Menu)
            {
                tabs.EscapeCurrentTab(playSound);
            }
        }

        // Injected mod items, drawn by vanilla like every other option.

        private static List<ListableOption> BuildMainColumnExtras()
        {
            var options = new List<ListableOption>
            {
                new ListableOption("RimWorldAccess.UI.Pause.PlaySettings".Translate(), delegate
                {
                    CloseRealMenuIfOpen();
                    PlaySettingsMenuState.Open();
                }),
            };
            // One item per registered mod HUD toolbar whose strip currently draws: those buttons
            // live outside any window, so this menu is their only keyboard doorway.
            foreach (ModHudToolbarRegistry.Toolbar toolbar in ModHudToolbarRegistry.Toolbars)
            {
                ModHudToolbarRegistry.Toolbar captured = toolbar;
                try
                {
                    if (!captured.Visible())
                    {
                        continue;
                    }
                    options.Add(new ListableOption(captured.Title(), delegate
                    {
                        CloseRealMenuIfOpen();
                        ModHudToolbarState.Open(captured.Id);
                    }));
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Mod HUD toolbar pause-menu item", ex);
                }
            }
            return options;
        }

        private static List<ListableOption> BuildLinksColumnExtras()
        {
            return new List<ListableOption>
            {
                new ListableOption_WebLink("RimWorldAccess.WhatsNew.MenuItem.WhatsNew".Translate(), delegate
                {
                    CloseRealMenuIfOpen();
                    WhatsNewState.Open();
                }, TexButton.IconBlog),
                new ListableOption_WebLink("RimWorldAccess.WhatsNew.MenuItem.Website".Translate(),
                    "https://rimworldaccess.com", TexButton.IconBlog),
            };
        }

        // Content model.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return MainButtonDefOf.Menu.LabelCap;
        }

        protected override int ContentItemCount(int region)
        {
            return entries.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= entries.Count)
            {
                return d;
            }
            d.Label = entries[index].Label;
            d.Role = ElementRole.MenuItem;
            // Vanilla registers no tips for these options, but any a mod adds is spoken for free.
            d.Extras = TooltipCapture.TryResolveAt(entries[index].GuiRect);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= entries.Count)
            {
                return;
            }
            MenuEntry entry = entries[index];
            rememberedLabel = entry.Label;
            Game current = Current.Game;
            rememberedSession = current != null ? new System.WeakReference<Game>(current) : null;
            if (entry.Activate != null)
            {
                entry.Activate();
            }
        }

        protected override void RefreshContent()
        {
            IReadOnlyList<CapturedListOption> captured = OptionListingCapture.Items;
            entries.Clear();
            for (int i = 0; i < captured.Count; i++)
            {
                CapturedListOption item = captured[i];
                Rect guiRect = new Rect(
                    lastInRect.x + item.Rect.x,
                    lastInRect.y + item.Rect.y,
                    item.Rect.width,
                    item.Rect.height);
                entries.Add(new MenuEntry(item.Option.label, ResolveActivate(item.Option),
                    guiRect, GuiSpace.VisibleScreenRect(guiRect)));
            }
        }

        /// <summary>A web-link option with no action opens its url on click; keyboard activation mirrors that.</summary>
        private static Action ResolveActivate(ListableOption option)
        {
            if (option.action == null)
            {
                ListableOption_WebLink link = option as ListableOption_WebLink;
                if (link != null && !string.IsNullOrEmpty(link.url))
                {
                    string url = link.url;
                    return delegate { Application.OpenURL(url); };
                }
            }
            return option.action;
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= entries.Count)
            {
                return default(Rect);
            }
            return entries[region.Index].ScreenRect;
        }

        // Per-GUI-pass work, driven by the window's own draw.

        /// <summary>
        /// Runs from the DoWindowContents postfix, inside the window's own GUI pass — the only place
        /// the option rects convert to screen space for the shared focus ring.
        /// </summary>
        internal void OnMenuGuiPass(Rect inRect)
        {
            lastInRect = inRect;
            RefreshModel();
            if (cursorRestorePending && entries.Count > 0)
            {
                cursorRestorePending = false;
                RestoreRememberedCursor();
            }
            // Hold a deferred announcement while a legacy overlay owns the keyboard: a swallowed
            // dialog resets its predecessor through the same Close path that nudges this scope, and
            // announcing under a just-opened dialog would talk over it.
            if (pendingAnnounce && entries.Count > 0 && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                pendingAnnounce = false;
                AnnounceCurrentItem();
            }
        }

        private void RestoreRememberedCursor()
        {
            // A different (or absent) Game invalidates the remembered cursor, so loading another
            // save starts at the top of the menu.
            Game current = Current.Game;
            Game tracked = null;
            if (rememberedSession != null)
            {
                rememberedSession.TryGetTarget(out tracked);
            }
            if (current == null || !ReferenceEquals(current, tracked))
            {
                rememberedLabel = null;
                rememberedSession = current != null ? new System.WeakReference<Game>(current) : null;
            }

            if (string.IsNullOrEmpty(rememberedLabel))
            {
                return;
            }
            ListModel region = Model.Region(0);
            if (region == null)
            {
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Label == rememberedLabel)
                {
                    region.MoveTo(i);
                    return;
                }
            }
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // Vanilla re-tests Cancel in the deferred window pass, so the stamp is what stops this
            // same Escape closing the menu twice.
            ShellFrameStamps.MarkCancelConsumed();
            // An explicit close forgets the remembered cursor.
            rememberedLabel = null;
            rememberedSession = null;
            CloseRealMenuIfOpen(playSound: true);
            TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
        }
    }

    /// <summary>
    /// Runs the scope's per-pass work inside the menu window's own GUI pass, bracketing the armed
    /// tooltip channel to that pass so only tips registered inside the menu window reach the
    /// describe path. The ring itself is the chassis's shared presenter.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Menu), "DoWindowContents")]
    public static class PauseMenuDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(out bool __state)
        {
            __state = false;
            try
            {
                if (FocusStack.Top is PauseMenuScope)
                {
                    __state = true;
                    TooltipCapture.BeginPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Pause menu draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(Rect rect, Exception __exception, bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                // Closed before the describe path reads the index: EndPass stops recording but
                // leaves it readable.
                TooltipCapture.EndPass();
                PauseMenuScope scope = __exception == null ? FocusStack.Top as PauseMenuScope : null;
                if (scope != null)
                {
                    scope.OnMenuGuiPass(rect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Pause menu draw pass error", ex);
            }
        }
    }

    /// <summary>Grows the menu tab enough that the links column still fits the mod's injected entries.</summary>
    [HarmonyPatch(typeof(MainTabWindow_Menu), "RequestedTabSize", MethodType.Getter)]
    public static class PauseMenuTabSizePatch
    {
        private const float InjectedItemsHeight = 70f;

        [HarmonyPostfix]
        public static void Postfix(ref Vector2 __result)
        {
            __result.y += InjectedItemsHeight;
        }
    }
}
