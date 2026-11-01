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
    /// Brackets ButtonTextCapture to a ScreenScope-owned window's own draw via
    /// Window.InnerWindowOnGUI — the private, non-virtual universal hook (see
    /// GenericWindowScope's remarks for the full rationale; this patch is its
    /// sibling and coexists the same way multiple taps share Widgets.ButtonText:
    /// each checks its own scope kind, and a window has at most one scope).
    /// </summary>
    [HarmonyPatch]
    internal static class ScreenScopeDrawPatch
    {
        private static readonly List<ScreenScope> active = new List<ScreenScope>();
        // Pairs the widget- and button-capture brackets' Prefix/Postfix explicitly
        // (the same capture-pass-leak fix GenericWindowDrawPatch uses): a keypress
        // can close the owning window mid-pass (Enter on a captured extras
        // Button through the live activation channel), popping the scope
        // before Postfix runs, so For(__instance) alone can no longer tell
        // Postfix it owes this pass a close. Tracked by WINDOW identity, not
        // by scope, so a scope that popped mid-pass is still detected.
        private static Window widgetCaptureOpenWindow;
        private static Window buttonCaptureOpenWindow;
        // Paired the same way, and deliberately on the pass the other brackets
        // skip: the tooltip harvest runs on LAYOUT, where IMGUI paints nothing,
        // so a certified Mouse.IsOver branch forced open builds its tooltip
        // string without putting a pixel on the screen. Prefix→Postfix wraps the
        // window's OWN Layout draw, so only registrations made inside that draw
        // (where HarvestWantsHover forces the certified Mouse.IsOver sites open)
        // can enter the harvest index; GUI drawn outside any window — the dev
        // toolbar, the time controls — can never record into it.
        private static Window harvestOpenWindow;

        // The listing ring rides its own registry rather than `active`: four of
        // its clients are plain FocusScope routers, and For(Window) filters on
        // capture opt-ins this arm has nothing to do with.
        private static readonly List<IListingRingClient> listingClients = new List<IListingRingClient>();
        private static Window listingRingOpenWindow;

        internal static void Register(ScreenScope scope)
        {
            if (!active.Contains(scope))
            {
                active.Add(scope);
            }
        }

        internal static void Unregister(ScreenScope scope)
        {
            active.Remove(scope);
        }

        internal static void RegisterListingClient(IListingRingClient client)
        {
            if (!listingClients.Contains(client))
            {
                listingClients.Add(client);
            }
        }

        internal static void UnregisterListingClient(IListingRingClient client)
        {
            listingClients.Remove(client);
        }

        private static IListingRingClient ListingClientFor(Window window)
        {
            if (window == null)
                return null;
            for (int i = 0; i < listingClients.Count; i++)
            {
                if (ReferenceEquals(listingClients[i].ListingRingWindow, window))
                {
                    return listingClients[i];
                }
            }
            return null;
        }

        internal static ScreenScope For(Window window)
        {
            if (window == null)
                return null;
            for (int i = 0; i < active.Count; i++)
            {
                ScreenScope scope = active[i];
                if (scope.OwnedWindow == window && (scope.WantsButtonCapture || scope.WantsWidgetCapture))
                {
                    return scope;
                }
            }
            return null;
        }

        /// <summary>
        /// The scope owning <paramref name="window"/>, WITHOUT For's capture opt-in
        /// filter: the content ring serves every scope on a real window, capture or not.
        /// </summary>
        private static ScreenScope ForContentRing(Window window)
        {
            if (window == null)
                return null;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].OwnedWindow == window)
                {
                    return active[i];
                }
            }
            return null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Window), "InnerWindowOnGUI", new Type[] { typeof(int) });
        }

        /// <summary>
        /// A Layout event never opens a capture pass, same rule and rationale
        /// as GenericWindowDrawPatch.IsLayoutPass: vanilla widgets may skip
        /// Layout outright (PawnTable.PawnTableOnGUI returns immediately), so
        /// a Layout capture is a starved remnant of the surface that clamps
        /// any count-driven cursor. The event type holds for the whole
        /// InnerWindowOnGUI call, so Prefix and Postfix skip in lockstep.
        /// </summary>
        private static bool IsLayoutPass()
        {
            return UnityEngine.Event.current != null
                && UnityEngine.Event.current.type == UnityEngine.EventType.Layout;
        }

        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            try
            {
                if (IsLayoutPass())
                {
                    ScreenScope layoutScope = For(__instance);
                    if (layoutScope != null && layoutScope.WantsWidgetCapture && TooltipCapture.BeginHarvestPass())
                    {
                        harvestOpenWindow = __instance;
                    }
                    return;
                }
                ScreenScope scope = For(__instance);
                if (scope != null && scope.WantsButtonCapture)
                {
                    ButtonTextCapture.BeginPass(scope.FocusedCaptureIndex(), scope);
                    buttonCaptureOpenWindow = __instance;
                }
                // Defensive guard (see WidgetCapture.IsPassOpen's remarks):
                // bespoke registration already stands the generic scope down
                // for this window, so this should never actually collide —
                // but a scope with no button capture must still not clobber
                // an unrelated in-flight pass.
                if (scope != null && scope.WantsWidgetCapture && !WidgetCapture.IsPassOpen)
                {
                    WidgetCapture.BeginPass(scope.FocusedWidgetCaptureIndex());
                    // The extras rows resolve each captured row's tooltip out of
                    // TooltipCapture's DETACHED index (CapturedRowFolder.FoldBand,
                    // shared with the inspect-tab capture harness), which only
                    // records between this bracket's own calls — without it every
                    // extras row on every opted-in screen silently loses the
                    // tooltip a sighted player reads on hover (the ideoligion
                    // surfaces alone register a dozen unconditionally, e.g.
                    // IdeoUIUtility's structure/style/randomize tips, and
                    // Page_CreateWorldParams' "PlanetCoverageTip"; a registration
                    // the CALLER wraps in its own Mouse.IsOver stays invisible to
                    // either channel — see TooltipCapture's remarks). Armed by the
                    // SAME guard that owns the widget pass, and closed by the same
                    // Postfix pairing, so the two brackets can never come apart.
                    TooltipCapture.BeginDetachedPass();
                    if (HoverSpeech.Enabled)
                    {
                        LiveTooltips.BeginBracket();
                    }
                    widgetCaptureOpenWindow = __instance;
                }
                IListingRingClient listingClient = ListingClientFor(__instance);
                if (listingClient != null && !ListingRowCapture.IsPassOpen)
                {
                    ListingRowCapture.BeginPass(listingClient.CurrentListingFocus());
                    listingRingOpenWindow = __instance;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Screen scope draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            try
            {
                if (IsLayoutPass())
                {
                    if (ReferenceEquals(harvestOpenWindow, __instance))
                    {
                        harvestOpenWindow = null;
                        TooltipCapture.EndHarvestPass();
                    }
                    return;
                }
                ScreenScope scope = For(__instance);
                if (ReferenceEquals(buttonCaptureOpenWindow, __instance))
                {
                    buttonCaptureOpenWindow = null;
                    if (scope != null && scope.WantsButtonCapture)
                    {
                        scope.OnButtonPassCompleted();
                    }
                    ButtonTextCapture.EndPass();
                }
                if (ReferenceEquals(widgetCaptureOpenWindow, __instance))
                {
                    widgetCaptureOpenWindow = null;
                    // Closed BEFORE the fold below: recording ends with the
                    // window's own draw, and the index stays readable afterwards
                    // (it is only cleared by the next BeginDetachedPass), which is
                    // exactly the contract FoldBand's same-frame resolution wants.
                    TooltipCapture.EndDetachedPass();
                    if (scope != null)
                    {
                        scope.OnWidgetPassCompleted();
                    }
                    HoverSpeech.EvaluateWidgetPass(detachedTips: true);
                    WidgetCapture.EndPass();
                }
                if (ReferenceEquals(listingRingOpenWindow, __instance))
                {
                    listingRingOpenWindow = null;
                    ListingRowCapture.EndPass();
                }
                ScreenScope ringScope = ForContentRing(__instance);
                if (ringScope != null)
                {
                    UnityEngine.Rect focused = ringScope.FocusedContentRect();
                    if (focused.width > 0f && focused.height > 0f)
                    {
                        FocusRing.Draw(GuiSpace.FromScreen(focused));
                        UiPointerFollow.NotifyFocusedRect(focused);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Screen scope draw pass error", ex);
            }
        }
    }
}
