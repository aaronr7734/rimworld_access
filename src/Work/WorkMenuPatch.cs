using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
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
            bool alt = KeyboardHelper.IsAltHeld;
            var typeahead = WorkMenuState.Typeahead;


            // Handle Escape - clear search first, then save & close
            if (key == KeyCode.Escape)
            {
                if (WorkMenuState.ClearSearchIfActive())
                {
                    Event.current.Use();
                    return;
                }
                WorkMenuState.Confirm();
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

            // Handle Ctrl+Tab (Option+Tab on macOS): Swap to table view.
            // IsCtrlHeld transparently substitutes Alt for Ctrl on Mac+Tab — see
            // KeyboardHelper.IsCtrlHeld for the cross-platform abstraction.
            if (key == KeyCode.Tab && KeyboardHelper.IsCtrlHeld)
            {
                WorkMenuOpener.SwapToTable();
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

            // Brackets cycle priority vanilla-style. Shift applies to all colonists.
            if (key == KeyCode.LeftBracket)
            {
                if (shift) WorkMenuState.CycleAllPawnsPriorityForCurrent(decrease: true);
                else WorkMenuState.CyclePriorityForCurrentEntry(decrease: true);
                Event.current.Use();
                return;
            }
            if (key == KeyCode.RightBracket)
            {
                if (shift) WorkMenuState.CycleAllPawnsPriorityForCurrent(decrease: false);
                else WorkMenuState.CyclePriorityForCurrentEntry(decrease: false);
                Event.current.Use();
                return;
            }

            // Handle Shift+0-4: Set priority for ALL compatible pawns in manual mode.
            // In basic mode, shift+digit is a no-op but still consumed so vanilla
            // TimeSpeed_* KeyBindingDefs can't interpret Shift+1/2/3 as game speed.
            if (shift && !alt)
            {
                bool isDigit04 =
                    key == KeyCode.Alpha0 || key == KeyCode.Keypad0 ||
                    key == KeyCode.Alpha1 || key == KeyCode.Keypad1 ||
                    key == KeyCode.Alpha2 || key == KeyCode.Keypad2 ||
                    key == KeyCode.Alpha3 || key == KeyCode.Keypad3 ||
                    key == KeyCode.Alpha4 || key == KeyCode.Keypad4;
                if (isDigit04)
                {
                    if (WorkMenuState.IsManualMode)
                    {
                        int digit =
                            (key == KeyCode.Alpha0 || key == KeyCode.Keypad0) ? 0 :
                            (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) ? 1 :
                            (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) ? 2 :
                            (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) ? 3 : 4;
                        WorkMenuState.SetPriorityForAllPawns(digit);
                    }
                    Event.current.Use();
                    return;
                }
            }

            // Handle number keys 0-4: Set priority (manual mode only).
            // In basic mode digits are no-ops (Space toggles, [ / ] cycle) but we
            // still consume them so vanilla time controls don't hear them.
            if (!alt && !shift)
            {
                bool isDigit04 =
                    key == KeyCode.Alpha0 || key == KeyCode.Keypad0 ||
                    key == KeyCode.Alpha1 || key == KeyCode.Keypad1 ||
                    key == KeyCode.Alpha2 || key == KeyCode.Keypad2 ||
                    key == KeyCode.Alpha3 || key == KeyCode.Keypad3 ||
                    key == KeyCode.Alpha4 || key == KeyCode.Keypad4;
                if (isDigit04)
                {
                    if (WorkMenuState.IsManualMode)
                    {
                        int digit =
                            (key == KeyCode.Alpha0 || key == KeyCode.Keypad0) ? 0 :
                            (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) ? 1 :
                            (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) ? 2 :
                            (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) ? 3 : 4;
                        WorkMenuState.SetPriority(digit);
                    }
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
                TypeaheadCharacterBuffer.RequestCharacter(c => WorkMenuState.ProcessSearchCharacter(c));
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

            string title = $"Work (Focused View) - {pawnName} ({pawnIndex}/{totalPawns}) - {mode}";

            string instructions1, instructions2, instructions3;

            if (WorkMenuState.IsManualMode)
            {
                instructions1 = "Up/Down: Switch priorities | Left/Right: Navigate tasks";
                instructions2 = "0-4: Set priority | Shift+0-4: Set for all | Tab/Shift+Tab: Switch pawn";
                instructions3 = "Enter/Escape: Save & close | Alt+M: Switch to basic mode";
            }
            else
            {
                instructions1 = "Left/Right: Navigate tasks | Space: Toggle";
                instructions2 = "Tab/Shift+Tab: Switch pawn";
                instructions3 = "Enter/Escape: Save & close | Alt+M: Switch to manual mode";
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

    /// <summary>
    /// Harmony patch to intercept the Work tab opening and replace it with our accessible version.
    /// Dispatches to either WorkMenuState (focused view) or WorkTableState (table view)
    /// based on the DefaultWorkMenuView setting.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class WorkWindowInterceptPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (WorkMenuState.IsActive || WorkTableState.IsActive)
            {
                Find.WindowStack.TryRemove(typeof(MainTabWindow_Work), doCloseSound: false);
                return false;
            }

            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            Pawn targetPawn = null;
            if (Find.Selector?.SelectedPawns?.Count > 0)
                targetPawn = Find.Selector.SelectedPawns.FirstOrDefault(p => p.IsColonist);
            if (targetPawn == null && Find.CurrentMap != null)
                targetPawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault();

            if (targetPawn != null)
            {
                WorkMenuOpener.OpenDefaultView(targetPawn);
                Find.WindowStack.TryRemove(typeof(MainTabWindow_Work), doCloseSound: false);
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Centralized dispatcher for opening the work menu. Reads the
    /// DefaultWorkMenuView mod setting and opens the matching view.
    /// </summary>
    public static class WorkMenuOpener
    {
        public static void OpenDefaultView(Pawn targetPawn)
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && settings.DefaultWorkMenuView == WorkMenuView.Table)
                WorkTableState.Open(targetPawn);
            else
                WorkMenuState.Open(targetPawn);
        }

        /// <summary>
        /// Swap from focused view to table view without saving/cancelling
        /// (changes are applied in real-time). Persists the chosen view.
        /// </summary>
        public static void SwapToTable()
        {
            if (!WorkMenuState.IsActive) return;
            Pawn currentPawn = WorkMenuState.CurrentPawn;
            WorkMenuState.CloseForSwap();
            RememberView(WorkMenuView.Table);
            WorkTableState.Open(currentPawn);
        }

        /// <summary>
        /// Swap from table view to focused view. Persists the chosen view.
        /// </summary>
        public static void SwapToFocused()
        {
            if (!WorkTableState.IsActive) return;
            Pawn currentPawn = WorkTableState.CurrentPawn;
            WorkTableState.CloseForSwap();
            RememberView(WorkMenuView.Focused);
            WorkMenuState.Open(currentPawn);
        }

        private static void RememberView(WorkMenuView view)
        {
            var mod = LoadedModManager.GetMod<RimWorldAccessMod_Settings>();
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null) return;
            if (settings.DefaultWorkMenuView == view) return;
            settings.DefaultWorkMenuView = view;
            mod?.WriteSettings();
        }
    }

    /// <summary>
    /// Harmony patch that intercepts keyboard input when the work table view is active.
    /// Separate from WorkMenuPatch to keep the two views' input-handling concerns apart.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class WorkTableMenuInputPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            if (!WorkTableState.IsActive) return;
            if (Event.current.type != EventType.KeyDown) return;

            KeyCode key = Event.current.keyCode;
            bool shift = Event.current.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;
            bool alt = KeyboardHelper.IsAltHeld;
            var typeahead = WorkTableState.Typeahead;

            // Ctrl+Tab (Option+Tab on macOS) — swap to focused view.
            // IsCtrlHeld transparently substitutes Alt for Ctrl on Mac+Tab — see
            // KeyboardHelper.IsCtrlHeld for the cross-platform abstraction.
            if (key == KeyCode.Tab && KeyboardHelper.IsCtrlHeld)
            {
                WorkMenuOpener.SwapToFocused();
                Event.current.Use();
                return;
            }

            // Escape — clear search first, otherwise save & close
            if (key == KeyCode.Escape)
            {
                if (WorkTableState.ClearSearchIfActive())
                {
                    Event.current.Use();
                    return;
                }
                WorkTableState.Confirm();
                Event.current.Use();
                return;
            }

            // Enter — save & close (or commit a search jump if active)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (typeahead != null && typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearch();
                    WorkTableState.AnnounceCurrentCell(includePawnName: true);
                }
                else
                {
                    WorkTableState.Confirm();
                }
                Event.current.Use();
                return;
            }

            if (key == KeyCode.Backspace)
            {
                if (WorkTableState.HandleBackspace())
                {
                    Event.current.Use();
                    return;
                }
            }

            if (alt && key == KeyCode.M)
            {
                WorkTableState.ToggleMode();
                Event.current.Use();
                return;
            }

            if (alt && key == KeyCode.S)
            {
                WorkTableState.ToggleSortByCurrentColumn();
                Event.current.Use();
                return;
            }

            // Painting: Ctrl+Shift+Home/End — paint entire column
            if ((key == KeyCode.Home || key == KeyCode.End) && ctrl && shift)
            {
                WorkTableState.PaintEntireColumn();
                Event.current.Use();
                return;
            }
            // Shift+Home/End — paint range toward start/end
            if (key == KeyCode.Home && shift)
            {
                WorkTableState.PaintToFirst();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End && shift)
            {
                WorkTableState.PaintToLast();
                Event.current.Use();
                return;
            }
            // Shift+Up/Down — paint single + move
            if (key == KeyCode.DownArrow && shift)
            {
                WorkTableState.PaintDown();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.UpArrow && shift)
            {
                WorkTableState.PaintUp();
                Event.current.Use();
                return;
            }

            // Navigation
            if (key == KeyCode.UpArrow)
            {
                WorkTableState.SelectPreviousPawn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow)
            {
                WorkTableState.SelectNextPawn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.LeftArrow)
            {
                WorkTableState.SelectPreviousColumn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.RightArrow)
            {
                WorkTableState.SelectNextColumn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Home)
            {
                WorkTableState.JumpToFirst();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End)
            {
                WorkTableState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Bracket cycling — [ decreases priority number (more important),
            // ] increases it (less important). Shift applies to every colonist.
            if (key == KeyCode.LeftBracket)
            {
                if (shift) WorkTableState.CycleAllColonistsPriority(decrease: true);
                else WorkTableState.CycleCurrentCellPriority(decrease: true);
                Event.current.Use();
                return;
            }
            if (key == KeyCode.RightBracket)
            {
                if (shift) WorkTableState.CycleAllColonistsPriority(decrease: false);
                else WorkTableState.CycleCurrentCellPriority(decrease: false);
                Event.current.Use();
                return;
            }

            // Number keys 0-4 — absolute priority for current cell (manual mode only).
            // In basic mode digits are no-ops (Space toggles, [ / ] cycle) but we
            // still consume them so vanilla time controls don't hear them.
            if (!alt && !shift && !ctrl)
            {
                int? digit = DigitFromKey(key);
                if (digit.HasValue && digit.Value <= 4)
                {
                    if (WorkTableState.IsManualMode)
                        WorkTableState.SetPriorityForCurrentCell(digit.Value);
                    Event.current.Use();
                    return;
                }
            }

            // Shift+0-4 — set priority for ALL eligible colonists in current column
            // (manual mode only; in basic mode we still consume to block vanilla
            // TimeSpeed_* KeyBindingDefs from interpreting Shift+1/2/3 as game speed).
            if (shift && !alt && !ctrl)
            {
                int? digit = DigitFromKey(key);
                if (digit.HasValue && digit.Value <= 4)
                {
                    if (WorkTableState.IsManualMode)
                        WorkTableState.SetPriorityForAllColonists(digit.Value);
                    Event.current.Use();
                    return;
                }
            }

            // Space — basic-mode toggle
            if (key == KeyCode.Space && !WorkTableState.IsManualMode)
            {
                WorkTableState.ToggleCurrentCell();
                Event.current.Use();
                return;
            }

            // Typeahead: letters match pawn names
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !alt && !shift && !ctrl)
            {
                TypeaheadCharacterBuffer.RequestCharacter(c => WorkTableState.HandleTypeahead(c));
                Event.current.Use();
                return;
            }

            // Consume * to prevent passthrough (matches focused view behavior)
            bool isStar = key == KeyCode.KeypadMultiply || (shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return;
            }
        }

        private static int? DigitFromKey(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return key - KeyCode.Alpha0;
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return key - KeyCode.Keypad0;
            return null;
        }
    }

    /// <summary>
    /// Draws a visual overlay indicating the work table view is active.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class WorkTableMenuOverlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!WorkTableState.IsActive) return;

            float screenWidth = UI.screenWidth;
            float overlayWidth = 800f;
            float overlayHeight = 140f;
            float overlayX = (screenWidth - overlayWidth) / 2f;
            float overlayY = 20f;

            Rect overlayRect = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);
            Widgets.DrawBoxSolid(overlayRect, new Color(0.1f, 0.1f, 0.1f, 0.9f));
            Widgets.DrawBox(overlayRect, 2);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            int pawnCount = WorkTableState.PawnCount;
            int row = WorkTableState.CurrentRowIndex + 1;
            string mode = WorkTableState.IsManualMode ? "Manual Priority Mode" : "Basic Mode";
            string colName = pawnCount > 0
                ? WorkTableState.TableHelper?.GetCurrentColumnName() ?? ""
                : "";
            string title = $"Work (Table View) - {mode} ({row}/{pawnCount}) - Column: {colName}";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 25f);
            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.8f, 0.9f, 1.0f);

            string line1 = "Up/Down: pawn | Left/Right: work type | 0-4: priority | [ ]: cycle priority";
            string line2 = "Shift+[ ]: cycle for all colonists | Shift+Arrows: paint | Alt+S: sort | Alt+M: mode";
            string line3 = $"{KeyboardHelper.CtrlLabel}+Tab: switch to focused view | Enter/Escape: save & close";

            Rect l1 = new Rect(overlayX, overlayY + 45f, overlayWidth, 22f);
            Rect l2 = new Rect(overlayX, overlayY + 70f, overlayWidth, 22f);
            Rect l3 = new Rect(overlayX, overlayY + 95f, overlayWidth, 22f);
            Widgets.Label(l1, line1);
            Widgets.Label(l2, line2);
            Widgets.Label(l3, line3);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
