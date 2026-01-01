using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input when the drug policy management interface is active.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class DrugPolicyPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard events when drug policy manager is active.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            // Only process if drug policy manager is active
            if (!WindowlessDrugPolicyState.IsActive)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            bool handled = false;
            KeyCode key = Event.current.keyCode;

            // Handle navigation based on current mode
            var mode = WindowlessDrugPolicyState.CurrentMode;

            if (mode == WindowlessDrugPolicyState.NavigationMode.PolicyList)
            {
                // Policy list mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessDrugPolicyState.SelectPreviousPolicy();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessDrugPolicyState.SelectNextPolicy();
                    handled = true;
                }
                else if (key == KeyCode.Tab && !Event.current.shift)
                {
                    WindowlessDrugPolicyState.EnterActionsMode();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDrugPolicyState.Close();
                    handled = true;
                }
            }
            else if (mode == WindowlessDrugPolicyState.NavigationMode.PolicyActions)
            {
                // Actions menu mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessDrugPolicyState.SelectPreviousAction();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessDrugPolicyState.SelectNextAction();
                    handled = true;
                }
                else if (key == KeyCode.Tab && !Event.current.shift)
                {
                    WindowlessDrugPolicyState.SelectNextAction();
                    handled = true;
                }
                else if (key == KeyCode.Tab && Event.current.shift)
                {
                    WindowlessDrugPolicyState.SelectPreviousAction();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessDrugPolicyState.ExecuteAction();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDrugPolicyState.ReturnToPolicyList();
                    handled = true;
                }
            }
            else if (mode == WindowlessDrugPolicyState.NavigationMode.DrugList)
            {
                // Drug list mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessDrugPolicyState.SelectPreviousDrug();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessDrugPolicyState.SelectNextDrug();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessDrugPolicyState.EditDrugSettings();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDrugPolicyState.ReturnToPolicyList();
                    handled = true;
                }
            }
            else if (mode == WindowlessDrugPolicyState.NavigationMode.DrugSettings)
            {
                // Drug settings mode
                if (key == KeyCode.UpArrow)
                {
                    WindowlessDrugPolicyState.SelectPreviousSetting();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessDrugPolicyState.SelectNextSetting();
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    WindowlessDrugPolicyState.ToggleSetting();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessDrugPolicyState.AdjustSetting(-1);
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessDrugPolicyState.AdjustSetting(1);
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessDrugPolicyState.ToggleSetting();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDrugPolicyState.ReturnToDrugList();
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
        /// Postfix patch that draws visual feedback for the drug policy manager.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only draw if drug policy manager is active
            if (!WindowlessDrugPolicyState.IsActive)
                return;

            DrawMenuOverlay();
        }

        /// <summary>
        /// Draws a visual overlay indicating the drug policy manager is active.
        /// </summary>
        private static void DrawMenuOverlay()
        {
            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create a rect for the overlay (top-center of screen)
            float overlayWidth = 800f;
            float overlayHeight = 140f;
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

            string title = "Drug Policy Manager";
            string instructions1 = "Up/Down: Navigate | Tab: Switch Mode | Enter: Execute/Toggle";
            string instructions2 = "Space: Toggle Boolean | Left/Right: Adjust Value | Esc: Back/Close";

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
