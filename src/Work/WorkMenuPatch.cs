using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input when the work menu is active.
    /// Supports grid-based navigation in manual mode and list navigation in basic mode.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class WorkMenuPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard events when work menu is active.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            if (!WorkMenuState.IsActive)
                return;

            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;
            bool shift = Event.current.shift;
            bool alt = Event.current.alt;
            var typeahead = WorkMenuState.Typeahead;

            // Handle Escape - clear search first, then cancel
            if (key == KeyCode.Escape)
            {
                if (WorkMenuState.ClearSearchIfActive())
                {
                    Event.current.Use();
                    return;
                }
                WorkMenuState.Cancel();
                Event.current.Use();
                return;
            }

            // Handle Enter/Return
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (WorkMenuState.SearchJumpPending)
                {
                    // Jump to the search result
                    WorkMenuState.JumpToSearchResult();
                }
                else
                {
                    // Confirm and close
                    WorkMenuState.Confirm();
                }
                Event.current.Use();
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace)
            {
                if (WorkMenuState.ProcessBackspace())
                {
                    Event.current.Use();
                    return;
                }
            }

            // Handle Alt+M: Toggle between basic and manual mode
            if (alt && key == KeyCode.M)
            {
                WorkMenuState.ToggleMode();
                Event.current.Use();
                return;
            }

            // Handle Tab: Switch pawns (saves current changes)
            if (key == KeyCode.Tab && !shift)
            {
                WorkMenuState.SwitchToNextPawn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Tab && shift)
            {
                WorkMenuState.SwitchToPreviousPawn();
                Event.current.Use();
                return;
            }

            // Handle Up/Down arrows (priority level navigation in manual mode, search navigation otherwise)
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    WorkMenuState.PreviousSearchMatch();
                }
                else
                {
                    WorkMenuState.MoveUp();
                }
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    WorkMenuState.NextSearchMatch();
                }
                else
                {
                    WorkMenuState.MoveDown();
                }
                Event.current.Use();
                return;
            }

            // Handle Left/Right arrows (task navigation within priority level)
            if (key == KeyCode.LeftArrow)
            {
                WorkMenuState.MoveLeft();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.RightArrow)
            {
                WorkMenuState.MoveRight();
                Event.current.Use();
                return;
            }

            // Handle Home/End: Jump to top/bottom of current column/list
            if (key == KeyCode.Home)
            {
                WorkMenuState.JumpToFirst();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End)
            {
                WorkMenuState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Handle number keys 0-4: Set priority (manual mode) or toggle (basic mode)
            if (!alt && !shift)
            {
                if (key == KeyCode.Alpha0 || key == KeyCode.Keypad0)
                {
                    WorkMenuState.SetPriority(0);
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1)
                {
                    WorkMenuState.SetPriority(1);
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2)
                {
                    WorkMenuState.SetPriority(2);
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3)
                {
                    WorkMenuState.SetPriority(3);
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Alpha4 || key == KeyCode.Keypad4)
                {
                    WorkMenuState.SetPriority(4);
                    Event.current.Use();
                    return;
                }
            }

            // Handle Space: Toggle selected work type (basic mode only)
            if (key == KeyCode.Space && !WorkMenuState.IsManualMode)
            {
                WorkMenuState.ToggleSelected();
                Event.current.Use();
                return;
            }

            // Handle type-ahead search characters (letters only, not numbers since 0-4 are for priorities)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !alt && !shift)
            {
                char c = (char)('a' + (key - KeyCode.A));
                WorkMenuState.ProcessSearchCharacter(c);
                Event.current.Use();
                return;
            }

            // Consume * to prevent passthrough
            bool isStar = key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return;
            }
        }

        /// <summary>
        /// Postfix patch that draws visual feedback for the work menu.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!WorkMenuState.IsActive)
                return;

            DrawMenuOverlay();
        }

        /// <summary>
        /// Draws a visual overlay indicating the work menu is active.
        /// </summary>
        private static void DrawMenuOverlay()
        {
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            float overlayWidth = 750f;
            float overlayHeight = 160f;
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

            string pawnName = WorkMenuState.CurrentPawn != null ? WorkMenuState.CurrentPawn.LabelShort : "Unknown";
            int pawnIndex = WorkMenuState.CurrentPawnIndex + 1;
            int totalPawns = WorkMenuState.TotalPawns;
            string mode = WorkMenuState.IsManualMode ? "Manual Priority Mode" : "Basic Mode";

            string title = $"Work Menu - {pawnName} ({pawnIndex}/{totalPawns}) - {mode}";

            string instructions1, instructions2, instructions3;

            if (WorkMenuState.IsManualMode)
            {
                instructions1 = "Up/Down: Switch priorities | Left/Right: Navigate tasks";
                instructions2 = "0-4: Set priority | Tab/Shift+Tab: Switch pawn";
                instructions3 = "Enter: Confirm | Escape: Cancel | Alt+M: Switch to basic mode";
            }
            else
            {
                instructions1 = "Left/Right: Navigate tasks | Space: Toggle";
                instructions2 = "Tab/Shift+Tab: Switch pawn";
                instructions3 = "Enter: Confirm | Escape: Cancel | Alt+M: Switch to manual mode";
            }

            // Show current position info
            string positionInfo = "";
            var entry = WorkMenuState.GetCurrentEntry();
            if (entry != null)
            {
                if (WorkMenuState.IsManualMode)
                {
                    var columns = WorkMenuState.GetColumns();
                    int colIndex = WorkMenuState.CurrentColumn;
                    string colName;
                    switch (colIndex)
                    {
                        case 0: colName = "Priority 1"; break;
                        case 1: colName = "Priority 2"; break;
                        case 2: colName = "Priority 3"; break;
                        case 3: colName = "Priority 4"; break;
                        case 4: colName = "Disabled"; break;
                        default: colName = "Unknown"; break;
                    }
                    int colCount = columns[colIndex].Count;
                    positionInfo = $"[{colName}: {WorkMenuState.CurrentRow + 1}/{colCount}] {entry.WorkType.labelShort}";
                }
                else
                {
                    int totalEntries = WorkMenuState.GetAllEntries().Count;
                    string status = entry.CurrentPriority > 0 ? "Enabled" : "Disabled";
                    positionInfo = $"[{status}] {entry.WorkType.labelShort}";
                }
            }

            // Search info
            var typeahead = WorkMenuState.Typeahead;
            if (typeahead.HasActiveSearch)
            {
                positionInfo = $"Search: '{typeahead.SearchBuffer}' - {typeahead.CurrentMatchPosition}/{typeahead.MatchCount} matches";
            }

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 25f);
            Rect positionRect = new Rect(overlayX, overlayY + 35f, overlayWidth, 25f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 65f, overlayWidth, 22f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 90f, overlayWidth, 22f);
            Rect instructions3Rect = new Rect(overlayX, overlayY + 115f, overlayWidth, 22f);

            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.8f, 0.9f, 1.0f);
            Widgets.Label(positionRect, positionInfo);
            GUI.color = Color.white;

            Widgets.Label(instructions1Rect, instructions1);
            Widgets.Label(instructions2Rect, instructions2);
            Widgets.Label(instructions3Rect, instructions3);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
