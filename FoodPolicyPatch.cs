using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input when the food policy management interface is active.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class FoodPolicyPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard events when food policy manager is active.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            // Only process if food policy manager is active
            if (!WindowlessFoodPolicyState.IsActive)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            bool handled = false;
            KeyCode key = Event.current.keyCode;

            // Check if we're in filter editing mode
            if (ThingFilterNavigationState.IsActive)
            {
                // Check if we're in slider editing submenu
                if (ThingFilterNavigationState.IsEditingSlider)
                {
                    // Slider editing mode - adjusting min/max
                    if (key == KeyCode.LeftArrow)
                    {
                        ThingFilterNavigationState.AdjustSlider(-1);
                        handled = true;
                    }
                    else if (key == KeyCode.RightArrow)
                    {
                        ThingFilterNavigationState.AdjustSlider(1);
                        handled = true;
                    }
                    else if (key == KeyCode.UpArrow || key == KeyCode.DownArrow)
                    {
                        // Toggle between Min and Max
                        ThingFilterNavigationState.ToggleSliderPart();
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        // Exit slider editing mode
                        ThingFilterNavigationState.ExitSliderEdit();
                        handled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        // Exit slider editing mode
                        ThingFilterNavigationState.ExitSliderEdit();
                        handled = true;
                    }
                }
                else
                {
                    // Normal filter navigation (not editing slider)
                    if (key == KeyCode.UpArrow)
                    {
                        ThingFilterNavigationState.SelectPrevious();
                        handled = true;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        ThingFilterNavigationState.SelectNext();
                        handled = true;
                    }
                    else if (key == KeyCode.Space)
                    {
                        ThingFilterNavigationState.ToggleSelected();
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        // Enter activates: sliders enter edit mode, categories expand/collapse, SaveAndReturn executes
                        ThingFilterNavigationState.ActivateSelected();
                        handled = true;
                    }
                    else if (key == KeyCode.LeftArrow)
                    {
                        // For categories, left arrow collapses; for sliders, announces value
                        ThingFilterNavigationState.ToggleExpand();
                        handled = true;
                    }
                    else if (key == KeyCode.RightArrow)
                    {
                        // For categories, right arrow expands; for sliders, announces value
                        ThingFilterNavigationState.ToggleExpand();
                        handled = true;
                    }
                    else if (key == KeyCode.A && Event.current.control)
                    {
                        ThingFilterNavigationState.AllowAll();
                        handled = true;
                    }
                    else if (key == KeyCode.D && Event.current.control)
                    {
                        ThingFilterNavigationState.DisallowAll();
                        handled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        // Exit filter mode, return to policy list
                        ThingFilterNavigationState.Deactivate();
                        WindowlessFoodPolicyState.ReturnToPolicyList();
                        handled = true;
                    }
                }
            }
            else
            {
                // Handle policy list and actions navigation based on current mode
                var mode = WindowlessFoodPolicyState.CurrentMode;

                if (mode == WindowlessFoodPolicyState.NavigationMode.PolicyList)
                {
                    // Policy list mode
                    if (key == KeyCode.UpArrow)
                    {
                        WindowlessFoodPolicyState.SelectPreviousPolicy();
                        handled = true;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        WindowlessFoodPolicyState.SelectNextPolicy();
                        handled = true;
                    }
                    else if (key == KeyCode.Tab && !Event.current.shift)
                    {
                        WindowlessFoodPolicyState.EnterActionsMode();
                        handled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        WindowlessFoodPolicyState.Close();
                        handled = true;
                    }
                }
                else if (mode == WindowlessFoodPolicyState.NavigationMode.PolicyActions)
                {
                    // Actions menu mode
                    if (key == KeyCode.UpArrow)
                    {
                        WindowlessFoodPolicyState.SelectPreviousAction();
                        handled = true;
                    }
                    else if (key == KeyCode.DownArrow)
                    {
                        WindowlessFoodPolicyState.SelectNextAction();
                        handled = true;
                    }
                    else if (key == KeyCode.Tab && !Event.current.shift)
                    {
                        WindowlessFoodPolicyState.SelectNextAction();
                        handled = true;
                    }
                    else if (key == KeyCode.Tab && Event.current.shift)
                    {
                        WindowlessFoodPolicyState.SelectPreviousAction();
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        WindowlessFoodPolicyState.ExecuteAction();
                        handled = true;
                    }
                    else if (key == KeyCode.Escape)
                    {
                        WindowlessFoodPolicyState.ReturnToPolicyList();
                        handled = true;
                    }
                }
            }

            // Consume the event if we handled it
            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Postfix patch that draws visual feedback for the food policy manager.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only draw if food policy manager is active
            if (!WindowlessFoodPolicyState.IsActive)
                return;

            DrawMenuOverlay();
        }

        /// <summary>
        /// Draws a visual overlay indicating the food policy manager is active.
        /// </summary>
        private static void DrawMenuOverlay()
        {
            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create a rect for the overlay (top-center of screen)
            float overlayWidth = 700f;
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

            string title = "Food Policy Manager";
            string instructions1 = "Up/Down: Navigate | Tab: Switch Mode | Enter: Execute Action";
            string instructions2 = "Space: Toggle Item | Ctrl+A: Allow All | Ctrl+D: Disallow All | Esc: Close";

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
