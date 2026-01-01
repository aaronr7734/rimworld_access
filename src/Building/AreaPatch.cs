using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input when the area management interface is active.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class AreaPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard events when area manager is active.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            // Only process if area manager is active
            if (!WindowlessAreaState.IsActive)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            bool handled = false;
            KeyCode key = Event.current.keyCode;

            // Handle navigation based on current mode
            var mode = WindowlessAreaState.CurrentMode;

            if (mode == WindowlessAreaState.NavigationMode.AreaList)
            {
                // Area list mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessAreaState.SelectPreviousArea();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessAreaState.SelectNextArea();
                    handled = true;
                }
                else if (key == KeyCode.Tab && !Event.current.shift)
                {
                    WindowlessAreaState.EnterActionsMode();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessAreaState.Close();
                    handled = true;
                }
            }
            else if (mode == WindowlessAreaState.NavigationMode.AreaActions)
            {
                // Actions menu mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessAreaState.SelectPreviousAction();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessAreaState.SelectNextAction();
                    handled = true;
                }
                else if (key == KeyCode.Tab && !Event.current.shift)
                {
                    WindowlessAreaState.SelectNextAction();
                    handled = true;
                }
                else if (key == KeyCode.Tab && Event.current.shift)
                {
                    WindowlessAreaState.SelectPreviousAction();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessAreaState.ExecuteAction();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessAreaState.ReturnToAreaList();
                    handled = true;
                }
            }

            // Consume the event if we handled it
            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Postfix patch that draws visual feedback for the area manager.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only draw if area manager is active
            if (!WindowlessAreaState.IsActive)
                return;

            DrawMenuOverlay();
        }

        /// <summary>
        /// Draws a visual overlay indicating the area manager is active.
        /// </summary>
        private static void DrawMenuOverlay()
        {
            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create a rect for the overlay (top-center of screen)
            float overlayWidth = 700f;
            float overlayHeight = 120f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);

            // Draw semi-transparent background
            Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Widgets.DrawBoxSolid(overlayRect, backgroundColor);

            // Draw border
            Color borderColor = new Color(0.5f, 0.7f, 1.0f, 1.0f);
            Widgets.DrawBox(overlayRect, 2);

            // Draw text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            string title = "Area Manager";
            string instructions1 = "Up/Down: Navigate | Tab: Switch Mode | Enter: Execute Action";
            string instructions2 = "Esc: Back/Close";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 30f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 50f, overlayWidth, 25f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 80f, overlayWidth, 25f);

            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            Widgets.Label(instructions1Rect, instructions1);
            Widgets.Label(instructions2Rect, instructions2);

            // Reset text anchor
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
