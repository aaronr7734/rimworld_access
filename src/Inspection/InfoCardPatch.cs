using System;
using System.Collections.Generic;
using HarmonyLib;
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
        private bool wasOpen;

        /// <param name="childTypes">
        /// Window types whose closing should hand focus back to the host. Defaults to
        /// <see cref="Dialog_InfoCard"/> when none are supplied.
        /// </param>
        public HostFocusReturn(params Type[] childTypes)
        {
            this.childTypes = (childTypes != null && childTypes.Length > 0)
                ? childTypes
                : new[] { typeof(Dialog_InfoCard) };
        }

        public void Track(Window host)
        {
            if (host == null || Find.WindowStack == null) return;
            bool open = AnyChildOpen();
            if (wasOpen && !open)
                Find.WindowStack.Notify_ManuallySetFocus(host);
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
    /// Uses PostOpen/PostClose lifecycle and delegates input handling to UnifiedKeyboardPatch.
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
        /// Keyboard input is handled by UnifiedKeyboardPatch, not here.
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
