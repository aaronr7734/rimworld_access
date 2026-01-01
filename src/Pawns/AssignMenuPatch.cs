using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input when the assign menu is active.
    /// Handles navigation (arrows), pawn switching (Tab), confirmation (Enter), and closing (Escape).
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class AssignMenuPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard events when assign menu is active.
        /// Returns false to prevent game from processing the event if we handle it.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            // Only process if assign menu is active
            if (!AssignMenuState.IsActive)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            bool handled = false;
            KeyCode key = Event.current.keyCode;
            bool shift = Event.current.shift;

            // Handle Home: Jump to first option in current column
            if (key == KeyCode.Home)
            {
                AssignMenuState.JumpToFirst();
                handled = true;
            }
            // Handle End: Jump to last option in current column
            else if (key == KeyCode.End)
            {
                AssignMenuState.JumpToLast();
                handled = true;
            }
            // Handle Escape: Clear search first if active, then close menu
            else if (key == KeyCode.Escape)
            {
                if (AssignMenuState.HasActiveSearch)
                {
                    AssignMenuState.ClearTypeaheadSearch();
                    handled = true;
                }
                else
                {
                    AssignMenuState.Close();
                    handled = true;
                }
            }
            // Handle Arrow Up: Navigate to previous option (with search awareness)
            else if (key == KeyCode.UpArrow)
            {
                if (AssignMenuState.HasActiveSearch && !AssignMenuState.HasNoMatches)
                {
                    AssignMenuState.SelectPreviousMatch();
                }
                else
                {
                    AssignMenuState.SelectPreviousOption();
                }
                handled = true;
            }
            // Handle Arrow Down: Navigate to next option (with search awareness)
            else if (key == KeyCode.DownArrow)
            {
                if (AssignMenuState.HasActiveSearch && !AssignMenuState.HasNoMatches)
                {
                    AssignMenuState.SelectNextMatch();
                }
                else
                {
                    AssignMenuState.SelectNextOption();
                }
                handled = true;
            }
            // Handle Arrow Left: Navigate to previous column
            else if (key == KeyCode.LeftArrow)
            {
                AssignMenuState.SelectPreviousColumn();
                handled = true;
            }
            // Handle Arrow Right: Navigate to next column
            else if (key == KeyCode.RightArrow)
            {
                AssignMenuState.SelectNextColumn();
                handled = true;
            }
            // Handle Tab: Switch to next pawn
            else if (key == KeyCode.Tab && !shift)
            {
                AssignMenuState.SwitchToNextPawn();
                handled = true;
            }
            // Handle Shift+Tab: Switch to previous pawn
            else if (key == KeyCode.Tab && shift)
            {
                AssignMenuState.SwitchToPreviousPawn();
                handled = true;
            }
            // Handle Enter/Return: Apply current selection to pawn
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                AssignMenuState.ApplySelection();
                handled = true;
            }
            // Handle Alt+E: Open management dialog for current column
            // (Alt required to avoid conflicting with typeahead 'e')
            else if (key == KeyCode.E && Event.current.alt)
            {
                AssignMenuState.OpenManagementDialog();
                handled = true;
            }

            // Consume the event if we handled it
            if (handled)
            {
                Event.current.Use();
            }
        }

        /// <summary>
        /// Postfix patch that draws visual feedback for the assign menu.
        /// Shows a highlighted overlay for the currently selected assignment.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only draw if assign menu is active
            if (!AssignMenuState.IsActive)
                return;

            // Draw a semi-transparent overlay to indicate menu is active
            DrawMenuOverlay();
        }

        /// <summary>
        /// Draws a visual overlay indicating the assign menu is active.
        /// Shows instructions and current selection.
        /// </summary>
        private static void DrawMenuOverlay()
        {
            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create a rect for the overlay (top-center of screen)
            float overlayWidth = 700f;
            float overlayHeight = 130f;
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

            string pawnName = AssignMenuState.CurrentPawn != null ? AssignMenuState.CurrentPawn.LabelShort : "Unknown";
            int pawnIndex = AssignMenuState.CurrentPawnIndex + 1;
            int totalPawns = AssignMenuState.TotalPawns;

            string title = $"Assign Menu - {pawnName} ({pawnIndex}/{totalPawns})";

            string instructions1 = "Left/Right: Switch Column | Up/Down: Navigate Options";
            string instructions2 = "Tab/Shift+Tab: Switch Pawn | E: Edit Policies | Enter: Apply | Esc: Close";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 30f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 45f, overlayWidth, 25f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 75f, overlayWidth, 25f);

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
