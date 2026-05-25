using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_FormCaravan to enable keyboard navigation.
    /// Activates CaravanFormationState when the dialog opens and handles keyboard input.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan))]
    public static class CaravanFormationPatch
    {
        /// <summary>
        /// Patch for Window.OnCancelKeyPressed to block the game's Escape key handling
        /// when overlay menus are active over caravan dialogs.
        /// This is a separate patch targeting the base Window class.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnCancelKeyPressed_Prefix(Window __instance)
        {
            // Block the game's Cancel handling for any window when WindowlessDialogState is active
            // The dialog (e.g., confirmation dialogs) should take priority over underlying windows
            if (WindowlessDialogState.IsActive)
            {
                return false; // Skip original method - let WindowlessDialogState handle the Escape
            }

            // Only intercept for caravan-related dialogs
            if (__instance is Dialog_FormCaravan || __instance is Dialog_SplitCaravan)
            {
                // Block the game's Cancel handling when our overlay menus are active
                if (QuantityMenuState.IsActive || WindowlessInspectionState.IsActive || StatBreakdownState.IsActive)
                {
                    return false; // Skip original method - let our overlay handle the Escape
                }

                // Block if typeahead search is active (let our handler clear the search first)
                if (__instance is Dialog_FormCaravan && CaravanFormationState.HasActiveTypeahead)
                {
                    return false;
                }
                if (__instance is Dialog_SplitCaravan && SplitCaravanState.HasActiveTypeahead)
                {
                    return false;
                }
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Patch for Window.OnAcceptKeyPressed to block the game's Enter key handling
        /// when our keyboard navigation is active over caravan dialogs.
        /// Dialog_SplitCaravan doesn't override OnAcceptKeyPressed, so it inherits from Window
        /// with closeOnAccept=true, causing Enter to close the dialog unexpectedly.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnAcceptKeyPressed_Prefix(Window __instance)
        {
            // Block the game's Accept handling when a windowless dialog is active.
            // Mirrors the existing guard in Window_OnCancelKeyPressed_Prefix.
            if (WindowlessDialogState.IsActive)
            {
                return false;
            }

            // Block Enter handling for Dialog_SplitCaravan when our state is active
            if (__instance is Dialog_SplitCaravan && SplitCaravanState.IsActive)
            {
                return false; // Skip original - our SplitCaravanState handles Enter key
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Patch for PostOpen to activate keyboard navigation when the dialog opens.
        /// Skips activation if route planner is being opened first (PendingRoutePlannerOpen flag).
        /// </summary>
        [HarmonyPatch("PostOpen")]
        [HarmonyPostfix]
        public static void PostOpen_Postfix(Dialog_FormCaravan __instance)
        {
            // If route planner is pending, skip activation - route planner will activate later
            // (this is the world-map "Form caravan" / C key flow, which opens it explicitly).
            if (CaravanFormationState.PendingRoutePlannerOpen)
            {
                // Clear the flag since we're now past the PostOpen stage
                CaravanFormationState.PendingRoutePlannerOpen = false;
                return;
            }

            // Non-reform caravan dialogs opened directly from a colony building gizmo (e.g. the
            // hitching-spot "Form caravan" command) start with no destination. The game's PostOpen
            // already started the route planner and removed this dialog from the window stack, so
            // route the player into the route planner to choose a destination first (matching
            // vanilla). After they confirm, the dialog reopens with a valid destination and the
            // branch below activates the formation tabs.
            if (ShouldChooseRouteFirst(__instance))
            {
                CaravanFormationState.BeginColonyOriginatedRouteChoice(__instance);
                return;
            }

            CaravanFormationState.Open(__instance);
        }

        /// <summary>
        /// True when a freshly-opened caravan dialog still needs the player to choose a destination
        /// route before it can be sent (e.g. opened from the hitching-spot gizmo). Excludes reform
        /// dialogs and the case where a destination has already been chosen (the dialog reopening
        /// after route confirmation).
        /// </summary>
        private static bool ShouldChooseRouteFirst(Dialog_FormCaravan dialog)
        {
            // Already choosing a route for this dialog - this is a re-entrant PostOpen.
            if (CaravanFormationState.IsColonyRouteChoiceActive)
                return false;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active || !planner.FormingCaravan)
                return false; // Route planner isn't driving this dialog.

            if (IsReform(dialog))
                return false; // Reform caravans run their own (often route-free) flow.

            // A destination already chosen means the dialog is reopening after route confirmation.
            FieldInfo destField = AccessTools.Field(typeof(Dialog_FormCaravan), "destinationTile");
            if (destField == null)
                return false;
            PlanetTile destination = (PlanetTile)destField.GetValue(dialog);
            return !destination.Valid;
        }

        /// <summary>
        /// Reads the dialog's private reform flag.
        /// </summary>
        private static bool IsReform(Dialog_FormCaravan dialog)
        {
            FieldInfo reformField = AccessTools.Field(typeof(Dialog_FormCaravan), "reform");
            return reformField != null && (bool)reformField.GetValue(dialog);
        }

        /// <summary>
        /// Patch for PostClose to deactivate keyboard navigation when the dialog closes.
        /// Don't close if we're in destination selection mode (dialog was temporarily removed).
        /// Announces cancellation unless send was attempted (game announces successful send).
        /// Also stops the route planner if it's still active - the game starts it in PostOpen
        /// for all caravan dialogs including reform, but doesn't stop it on close.
        /// </summary>
        [HarmonyPatch("PostClose")]
        [HarmonyPostfix]
        public static void PostClose_Postfix(Dialog_FormCaravan __instance)
        {
            // Don't close state if we're choosing a destination - we'll reopen the dialog
            // Check both our flag AND the game's choosingRoute flag (set by WorldRoutePlanner.Start)
            if (CaravanFormationState.IsChoosingDestination || __instance.choosingRoute)
                return;

            // Capture send state before Close() resets it
            bool wasSendAttempted = CaravanFormationState.SendAttempted;

            CaravanFormationState.Close();

            // IMPORTANT: Stop the route planner if it's still active
            // The game starts WorldRoutePlanner in PostOpen for ALL caravan dialogs (including reform)
            // but doesn't stop it when the dialog closes after successful send.
            // This leaves a stale dialog reference that causes soft locks if user presses Enter on world map.
            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
            {
                Find.WorldRoutePlanner.Stop();
            }

            // Announce cancellation only if user didn't attempt to send
            // (successful send is announced by the game itself)
            if (!wasSendAttempted)
            {
                TolkHelper.Speak("Caravan formation cancelled");
            }
        }

        /// <summary>
        /// Patch for Notify_NoLongerChoosingRoute to clean up state when route planner stops.
        /// Does NOT announce cancellation here - that's handled by PostClose_Postfix.
        /// This avoids false positives when the route planner stops due to timing issues
        /// during automatic startup (game starts route planner immediately on dialog open).
        /// </summary>
        [HarmonyPatch("Notify_NoLongerChoosingRoute")]
        [HarmonyPostfix]
        public static void Notify_NoLongerChoosingRoute_Postfix(Dialog_FormCaravan __instance)
        {
            // Only clean up state if the dialog is not being reopened
            // Don't announce here - let PostClose_Postfix handle announcements
            // This prevents false "cancelled" announcements during route planner startup timing issues
            if (!Find.WindowStack.IsOpen(__instance))
            {
                // Just clean up state silently - the dialog will handle proper announcements
                CaravanFormationState.Close();
            }
        }

        /// <summary>
        /// Patch for OnAcceptKeyPressed to block the game's Enter key handling when our keyboard nav is active.
        /// Without this, Enter key triggers TrySend() which shows validation errors.
        /// </summary>
        [HarmonyPatch("OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool OnAcceptKeyPressed_Prefix()
        {
            // Block the game's handling when our keyboard nav is active
            // Our code in CaravanFormationState.HandleInput handles Enter for inspection/quantity
            if (CaravanFormationState.IsActive)
            {
                return false; // Skip original method
            }
            return true; // Let original method run
        }


        /// <summary>
        /// Patch for DoWindowContents to draw visual indicators.
        /// Keyboard input is handled by UnifiedKeyboardPatch at Normal priority on UIRootOnGUI,
        /// which runs BEFORE DoWindowContents for proper input handling.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_FormCaravan __instance, Rect inRect)
        {
            if (!CaravanFormationState.IsActive)
                return;

            // Draw visual indicator that keyboard mode is active
            DrawKeyboardModeIndicator(inRect);
        }

        /// <summary>
        /// Draws a visual indicator at the top of the dialog showing that keyboard mode is active.
        /// </summary>
        private static void DrawKeyboardModeIndicator(Rect inRect)
        {
            // Draw indicator in top-left corner
            float indicatorWidth = 250f;
            float indicatorHeight = 30f;
            Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

            // Draw background
            Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.4f, 0.6f, 1.0f, 1.0f);
            Widgets.DrawBox(indicatorRect, 1);

            // Draw text
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(indicatorRect, "Keyboard Mode Active");

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Draw instructions below the indicator
            float instructionsY = indicatorRect.yMax + 5f;
            float instructionsWidth = 500f;
            float instructionsHeight = 60f;
            Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            string instructions = "Tabs: Pawns, Items, Travel Supplies | Space/Enter: Toggle/Quantity\n" +
                                "Shift+Enter: Max | Del: Remove | Tab: Summary | Alt+I: Inspect/Breakdown\n" +
                                "Alt+A: Auto-provision | Alt+D: Destination | Alt+S: Send | Alt+R: Reset";

            Widgets.Label(instructionsRect, instructions);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
