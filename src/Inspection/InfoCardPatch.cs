using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Restores IMGUI keyboard focus to a directly-patched host screen (the ideoligion preset list,
    /// builder, or reform dialog) after a tracked child window opened over it closes. By default it
    /// tracks <see cref="Dialog_InfoCard"/>; pass additional window types to the constructor to also
    /// reclaim focus after they close (e.g. the saved-ideoligion load picker).
    ///
    /// RimWorld draws each window via <c>GUI.Window</c>, so a window only receives KeyDown while it
    /// holds GUI focus. Reclaiming that focus only takes effect when done inside the host's OWN
    /// DoWindowContents / GUI.Window pass — doing it from the child's PostClose (a different GUI
    /// context) is no better than the WindowStack's own handling and leaves the host dead to input.
    /// So each host calls <see cref="Track"/> every frame from its DoWindowContents prefix; when a
    /// tracked child that was open is now gone, focus is reclaimed on the host.
    /// </summary>
    public sealed class HostFocusReturn
    {
        private readonly Type[] childTypes;
        private readonly Action onChildClosed;
        private bool wasOpen;

        /// <param name="childTypes">
        /// Window types whose closing should hand focus back to the host. Defaults to
        /// <see cref="Dialog_InfoCard"/> when none are supplied.
        /// </param>
        public HostFocusReturn(params Type[] childTypes)
            : this(null, childTypes)
        {
        }

        /// <param name="onChildClosed">
        /// Optional callback invoked (after focus is reclaimed) the frame a tracked child window
        /// closes — e.g. to rebuild the host's list and re-announce where the cursor landed, so the
        /// player knows they are back on the host screen. Generalizes the "returned from sub-editor"
        /// rebuild/re-announce that directly-patched hosts otherwise hand-roll.
        /// </param>
        /// <param name="childTypes">See the other constructor.</param>
        public HostFocusReturn(Action onChildClosed, params Type[] childTypes)
        {
            this.onChildClosed = onChildClosed;
            this.childTypes = (childTypes != null && childTypes.Length > 0)
                ? childTypes
                : new[] { typeof(Dialog_InfoCard) };
        }

        /// <summary>True while at least one tracked child window is on the stack.</summary>
        public bool AnyOpen => AnyChildOpen();

        public void Track(Window host)
        {
            if (host == null || Find.WindowStack == null) return;
            bool open = AnyChildOpen();
            if (wasOpen && !open)
            {
                Find.WindowStack.Notify_ManuallySetFocus(host);
                onChildClosed?.Invoke();
            }
            wasOpen = open;
        }

        private bool AnyChildOpen()
        {
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                for (int j = 0; j < childTypes.Length; j++)
                {
                    if (childTypes[j].IsInstanceOfType(windows[i]))
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Harmony patches for Dialog_InfoCard to enable keyboard accessibility.
    /// Uses PostOpen/PostClose lifecycle and delegates input handling to the shell scope.
    /// </summary>
    public static class InfoCardPatch
    {
        // Track the current dialog for delayed initialization (stats need a few frames to populate)
        private static Dialog_InfoCard currentDialog = null;
        private static int framesSinceOpen = 0;
        private static bool hasAnnounced = false;

        /// <summary>
        /// Postfix patch for DoWindowContents to handle delayed initialization.
        /// Stats aren't populated until a few frames after opening, so we wait before announcing.
        /// Keyboard input is handled by InfoCardScope, not here.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_InfoCard), "DoWindowContents")]
        public static class DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_InfoCard __instance, Rect inRect)
            {
                // Only process if this is the dialog we're tracking
                if (currentDialog != __instance)
                    return;

                framesSinceOpen++;

                // Wait a few frames for stats to populate, then announce and rebuild
                if (!hasAnnounced && framesSinceOpen >= 3)
                {
                    hasAnnounced = true;
                    InfoCardState.RebuildAndAnnounce();
                }
            }
        }

        /// <summary>
        /// Vanilla's stat cache is shared and never sees two cards stacked; ours stack for
        /// Escape-back. The current card owns the cache, any other card draws into its own.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_InfoCard), "DoWindowContents")]
        public static class DoWindowContents_CacheOwnershipPatch
        {
            // Swapped by reference: FinalizeCachedDrawEntries reassigns the list, and the values
            // are read by row index.
            private static readonly AccessTools.FieldRef<List<StatDrawEntry>> entriesRef =
                AccessTools.StaticFieldRefAccess<List<StatDrawEntry>>(AccessTools.Field(typeof(StatsReportUtility), "cachedDrawEntries"));

            private static readonly AccessTools.FieldRef<List<string>> valuesRef =
                AccessTools.StaticFieldRefAccess<List<string>>(AccessTools.Field(typeof(StatsReportUtility), "cachedEntryValues"));

            private static ref List<StatDrawEntry> Entries => ref entriesRef();

            private static ref List<string> Values => ref valuesRef();

            private sealed class StatCache
            {
                internal List<StatDrawEntry> entries = new List<StatDrawEntry>();
                internal List<string> values = new List<string>();
            }

            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Dialog_InfoCard, StatCache> parked =
                new System.Runtime.CompilerServices.ConditionalWeakTable<Dialog_InfoCard, StatCache>();

            private static readonly StatCache ownerCache = new StatCache();

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static void Prefix(Dialog_InfoCard __instance, out bool __state)
            {
                __state = false;
                Dialog_InfoCard owner = InfoCardState.CurrentDialog;
                StatCache own;
                if (owner == null || owner == __instance)
                {
                    if (parked.TryGetValue(__instance, out own))
                    {
                        Entries = own.entries;
                        Values = own.values;
                        parked.Remove(__instance);
                    }
                    return;
                }

                ownerCache.entries = Entries;
                ownerCache.values = Values;
                if (!parked.TryGetValue(__instance, out own))
                {
                    own = new StatCache();
                    parked.Add(__instance, own);
                }
                Entries = own.entries;
                Values = own.values;
                __state = true;
            }

            [HarmonyFinalizer]
            public static void Finalizer(Dialog_InfoCard __instance, bool __state)
            {
                if (!__state)
                    return;

                if (parked.TryGetValue(__instance, out StatCache own))
                {
                    own.entries = Entries;
                    own.values = Values;
                }
                Entries = ownerCache.entries;
                Values = ownerCache.values;
                ownerCache.entries = null;
                ownerCache.values = null;
            }
        }

        /// <summary>
        /// Prefix patch for DoWindowContents that drives vanilla's own tab and stat
        /// selection from the accessible tree's cursor (see
        /// <see cref="InfoCardVisualDriver"/>). Must run as a PREFIX: the postfix above
        /// runs after FillCard has already drawn (decompiled Verse/Dialog_InfoCard.cs:570),
        /// which is too late for either nudge to be visible this frame.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_InfoCard), "DoWindowContents")]
        public static class DoWindowContents_DriverPatch
        {
            [HarmonyPrefix]
            public static void Prefix(Dialog_InfoCard __instance)
            {
                try
                {
                    InfoCardVisualDriver.Drive(__instance);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Info card visual driver prefix error", ex);
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostOpen to activate keyboard navigation when Dialog_InfoCard opens.
        /// We patch the base Window class since Dialog_InfoCard doesn't override PostOpen.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_InfoCard dialog)
                {
                    // Reset state for new dialog
                    currentDialog = dialog;
                    framesSinceOpen = 0;
                    hasAnnounced = false;

                    // Initialize state but don't announce yet (stats need to load)
                    InfoCardState.Open(dialog, announceOpening: false);
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when Dialog_InfoCard closes.
        /// We patch the base Window class since Dialog_InfoCard doesn't override PostClose.
        /// PostClose is called by WindowStack.TryRemove after the window is removed from the stack.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_InfoCard))
                    return;

                // A nested info card may remain underneath this one.
                Dialog_InfoCard remainingCard = null;
                if (Find.WindowStack != null)
                {
                    foreach (var window in Find.WindowStack.Windows)
                    {
                        if (window is Dialog_InfoCard card && card != __instance)
                        {
                            remainingCard = card;
                            break;
                        }
                    }
                }

                if (InfoCardState.IsClosingFromAccessibility)
                {
                    // CloseInfoCard manages its own state; just update our DoWindowContents tracking.
                    currentDialog = null;
                    framesSinceOpen = 0;
                    hasAnnounced = false;
                }
                else if (remainingCard != null)
                {
                    // Restore outer card from saved state if available, otherwise re-init
                    currentDialog = remainingCard;
                    framesSinceOpen = 0;
                    hasAnnounced = false;
                    if (InfoCardState.HasSavedState)
                    {
                        InfoCardState.RestoreFromStack(remainingCard);
                    }
                    else
                    {
                        InfoCardState.Open(remainingCard, announceOpening: false);
                    }
                }
                else
                {
                    currentDialog = null;
                    framesSinceOpen = 0;
                    hasAnnounced = false;
                    InfoCardState.ClearStack();
                    if (InfoCardState.IsActive)
                    {
                        InfoCardState.Close();
                    }
                    // Return to whatever opened the info card (inspection tree row, etc.)
                    InspectionReturnHelper.AnnounceParentOrFallback(null);
                }

                // NOTE: focus restoration for directly-patched host screens (ideoligion preset list /
                // builder / reform) is NOT done here — reclaiming focus during the info card's removal
                // is the wrong GUI context for GUI.FocusWindow to take effect. Each host reclaims focus
                // from its own DoWindowContents instead (see HostFocusReturn).
            }
        }
    }
}
