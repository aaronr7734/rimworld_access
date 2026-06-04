using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for MainTabWindow_History to enable keyboard accessibility.
    /// Activates HistoryState when the History tab opens and handles keyboard input.
    /// Note: MainTabWindow_History doesn't override PostOpen/PostClose, so we patch Window directly.
    /// </summary>
    [HarmonyPatch]
    public static class HistoryPatch
    {
        /// <summary>
        /// Postfix patch that runs after Window.PostOpen().
        /// Opens the keyboard navigation interface when MainTabWindow_History opens.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        [HarmonyPostfix]
        public static void Window_PostOpen_Postfix(Window __instance)
        {
            if (__instance is MainTabWindow_History)
            {
                HistoryState.Open();
            }
        }

        /// <summary>
        /// Prefix patch for Window.Close to clean up our state when MainTabWindow_History closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "Close")]
        [HarmonyPrefix]
        public static void Window_Close_Prefix(Window __instance)
        {
            if (__instance is MainTabWindow_History)
            {
                if (HistoryState.IsActive)
                {
                    HistoryState.Close();
                    TolkHelper.Speak("RimWorldAccess.History.Closed".Loc());
                }
            }
        }

        /// <summary>
        /// Patch for Window.OnCancelKeyPressed to block RimWorld's Escape key handling
        /// when our accessibility states are active over the History window.
        ///
        /// CRITICAL: RimWorld's Window.OnCancelKeyPressed runs independently of our
        /// UnifiedKeyboardPatch. Event.current.Use() does NOT block it. The only way
        /// to prevent double-close bugs is to patch the method directly.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnCancelKeyPressed_Prefix(Window __instance)
        {
            // Only intercept for History window
            if (!(__instance is MainTabWindow_History))
                return true;

            // Block the game's Cancel handling when our sub-states are active
            // This prevents the window from closing when user presses Escape to:
            // 1. Clear typeahead search
            // 2. Go back from detail view to list view in Messages tab
            if (HistoryStatisticsState.IsActive || HistoryMessagesState.IsActive)
            {
                // Check if there's something to handle first:
                // - Active typeahead search that needs to be cleared
                // - Detail view that needs to exit to list view
                if (HistoryState.HasActiveTypeahead)
                {
                    return false; // Block - our handler will clear the search
                }

                if (HistoryMessagesState.IsActive && HistoryMessagesState.IsInDetailView)
                {
                    return false; // Block - our handler will go back to list view
                }
            }

            // Let original method run (will close the window)
            return true;
        }

        /// <summary>
        /// Patch for Window.OnAcceptKeyPressed to block RimWorld's Enter key handling
        /// when our accessibility states are active over the History window.
        ///
        /// CRITICAL: RimWorld's default Window behavior closes the window on Enter
        /// (closeOnAccept = true). We need to block this when navigating within our menus.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnAcceptKeyPressed_Prefix(Window __instance)
        {
            // Only intercept for History window
            if (!(__instance is MainTabWindow_History))
                return true;

            // Block the game's Accept handling when our sub-states are active
            // This prevents the window from closing when user presses Enter to:
            // 1. Enter detail view
            // 2. Activate a button
            if (HistoryStatisticsState.IsActive || HistoryMessagesState.IsActive)
            {
                return false; // Block - our handler will process Enter
            }

            // Let original method run (will close the window)
            return true;
        }

        /// <summary>
        /// Patch for DoWindowContents to draw visual indicators when keyboard mode is active.
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_History), "DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(MainTabWindow_History __instance, Rect rect)
        {
            if (!HistoryState.IsActive)
                return;

            DrawKeyboardModeIndicator(rect);
        }

        /// <summary>
        /// Draws a visual indicator at the top of the window showing that keyboard mode is active.
        /// </summary>
        private static void DrawKeyboardModeIndicator(Rect inRect)
        {
            // Draw indicator in top-left corner (below the tab bar)
            float indicatorWidth = 300f;
            float indicatorHeight = 30f;
            Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 40f, indicatorWidth, indicatorHeight);

            // Draw background
            Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.4f, 0.6f, 1.0f, 1.0f);
            Widgets.DrawBox(indicatorRect, 1);

            // Draw text
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(indicatorRect, $"Keyboard Mode Active - {HistoryState.GetTabName()} Tab");

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Draw instructions below the indicator
            float instructionsY = indicatorRect.yMax + 5f;
            float instructionsWidth = 500f;
            float instructionsHeight = 45f;
            Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            string instructions = "Tab/Shift+Tab: Switch tabs | Up/Down: Navigate | Enter: Details\n";

            if (HistoryState.CurrentTab == HistoryState.Tab.Messages)
            {
                instructions += "Alt+L: Toggle Letters | Alt+M: Toggle Messages | Alt+P: Pin | Alt+J: Jump";
            }
            else
            {
                instructions += "Type to search | Home/End: First/Last";
            }

            Widgets.Label(instructionsRect, instructions);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
