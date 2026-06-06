using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for map portal loading accessibility (<see cref="Dialog_EnterPortal"/>).
    ///
    /// Map portals - ancient complex entrances (<c>AncientHatch</c>), Anomaly pit gates
    /// (<c>PitGate</c>), insect lair entrances, and pocket-map exits (<c>PocketMapExit</c>) -
    /// all open this dialog from <c>MapPortal.GetGizmos</c> to load pawns and items before
    /// entering or exiting. The dialog is structurally identical to Dialog_LoadTransporters,
    /// so it reuses <see cref="TransportPodLoadingState"/> via the
    /// <see cref="EnterPortalAdapter"/>.
    /// </summary>
    public static class PortalPatch
    {
        /// <summary>
        /// Patch for Dialog_EnterPortal.PostOpen to initialize accessibility state.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_EnterPortal))]
        [HarmonyPatch("PostOpen")]
        public static class Dialog_EnterPortal_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_EnterPortal __instance)
            {
                TransportPodLoadingState.Open(__instance);
            }
        }

        /// <summary>
        /// Patch for Window.PostClose to clean up accessibility state when Dialog_EnterPortal
        /// closes. We patch Window.PostClose (not Dialog_EnterPortal.PostClose) because
        /// Dialog_EnterPortal does NOT override PostClose - it inherits from Window, and
        /// patching a non-existent method on the derived class silently fails.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_EnterPortal_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_EnterPortal))
                    return;

                // Capture accept state and the cancel announcement before Close() clears them.
                bool wasAccepted = TransportPodLoadingState.AcceptAttempted;
                string cancelAnnouncement = TransportPodLoadingState.CancelAnnouncement;

                TransportPodLoadingState.Close();

                // Only announce cancellation if the user didn't accept (the game announces
                // a successful load itself).
                if (!wasAccepted)
                {
                    TolkHelper.Speak(cancelAnnouncement, SpeechPriority.Normal);
                }
            }
        }

        /// <summary>
        /// Patch for Window.OnCancelKeyPressed to block the game's Escape handling when our
        /// overlay menus or typeahead search are active over the portal dialog.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_EnterPortal_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_EnterPortal))
                    return true;

                // Block the game's Cancel handling when our overlay menus are active.
                if (QuantityMenuState.IsActive || WindowlessInspectionState.IsActive || StatBreakdownState.IsActive)
                {
                    return false;
                }

                // Block if typeahead search is active so Escape clears the search instead.
                if (TransportPodLoadingState.HasActiveTypeahead)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Patch for Dialog_EnterPortal.OnAcceptKeyPressed to block the game's Enter key handling
        /// while our keyboard navigation is active. Without this, Enter would immediately confirm
        /// loading instead of toggling the selected pawn / adjusting an item quantity.
        /// Event.current.Use() does NOT block RimWorld's KeyBindingDef.Accept handling.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_EnterPortal))]
        [HarmonyPatch("OnAcceptKeyPressed")]
        public static class Dialog_EnterPortal_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                // Allow through when our own Accept() is invoking OnAcceptKeyPressed.
                if (TransportPodLoadingState.AcceptingFromOurCode)
                {
                    return true;
                }

                if (TransportPodLoadingState.IsActive)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Patch for Dialog_EnterPortal.DoWindowContents to draw a visual indicator that
        /// keyboard navigation is active (mirrors the transport pod loading dialog).
        /// </summary>
        [HarmonyPatch(typeof(Dialog_EnterPortal))]
        [HarmonyPatch("DoWindowContents")]
        public static class Dialog_EnterPortal_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Rect inRect)
            {
                if (!TransportPodLoadingState.IsActive)
                    return;

                float indicatorWidth = 250f;
                float indicatorHeight = 30f;
                Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

                Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
                Widgets.DrawBoxSolid(indicatorRect, backgroundColor);
                Widgets.DrawBox(indicatorRect, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, "Keyboard Mode Active");

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                float instructionsY = indicatorRect.yMax + 5f;
                Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, 400f, 45f);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;

                string instructions = "Left/Right: Tabs | Up/Down: Items | Enter/Space: Select\n" +
                                    "Alt+I: Inspect | Alt+S: Confirm | Esc: Cancel";
                Widgets.Label(instructionsRect, instructions);

                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }
    }
}
