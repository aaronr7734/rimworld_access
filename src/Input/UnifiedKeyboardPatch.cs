using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Unified Harmony patch for UIRoot.UIRootOnGUI to handle all keyboard accessibility features.
    /// Handles: Escape key for pause menu, Enter key for building inspection/beds, ] key for colonist orders, I key for inspection menu, J key for scanner, L key for notification menu, F7 key for quest menu, Alt+M for mood info, Alt+H for health info, Alt+N for needs info, Alt+F for unforbid all items, Alt+Home for scanner auto-jump toggle, Shift+C for reform caravan (temporary maps), F2 for schedule, F3 for assign, F6 for research, and all windowless menu navigation.
    /// Note: Dialog navigation (including research completion dialogs) is handled by DialogAccessibilityPatch.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class UnifiedKeyboardPatch
    {
        /// <summary>
        /// Prefix patch that intercepts keyboard input for all accessibility features.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Skip if no actual key (Unity IMGUI quirk)
            if (key == KeyCode.None)
                return;

            // ===== PRIORITY -1: Block ALL keys if zone rename is active =====
            // Zone rename needs to capture text input, so block everything here
            // StorageSettingsMenuPatch will handle the input
            if (ZoneRenameState.IsActive)
            {
                // Don't process any keys in this patch when renaming
                return;
            }

            // ===== PRIORITY -0.5: Block game hotkeys if windowless dialog is active =====
            // WindowlessDialogInputPatch handles navigation keys for the dialog
            // We need to block game-specific keys (R for draft, F for forbid, etc.)
            if (WindowlessDialogState.IsActive)
            {
                // If editing a text field, block EVERYTHING except the text field control keys
                // Text input characters will be handled by WindowlessDialogInputPatch
                if (WindowlessDialogState.IsEditingTextField)
                {
                    // Only allow Enter, Escape, Backspace, Delete for text field control
                    // Block ALL other keys including character input (will be handled by WindowlessDialogInputPatch)
                    if (key != KeyCode.Return && key != KeyCode.KeypadEnter &&
                        key != KeyCode.Escape && key != KeyCode.Backspace &&
                        key != KeyCode.Delete)
                    {
                        // Consume the event to prevent it from being processed by RimWorld's keybinding system
                        // This is critical for keys like Space (pause/unpause), F5 (save), and other game hotkeys
                        Event.current.Use();
                        return;
                    }
                }
                else
                {
                    // Not editing - allow arrow keys and Enter/Escape for dialog navigation
                    // These keys will be handled by WindowlessDialogInputPatch (VeryHigh priority)
                    // Block everything else (R, F, A, Z, Tab, etc.)
                    if (key != KeyCode.UpArrow && key != KeyCode.DownArrow &&
                        key != KeyCode.LeftArrow && key != KeyCode.RightArrow &&
                        key != KeyCode.Return && key != KeyCode.KeypadEnter &&
                        key != KeyCode.Escape)
                    {
                        // Consume the event to prevent game actions during dialog
                        Event.current.Use();
                        return;
                    }
                    // If we reach here, key is arrow/Enter/Escape for dialog navigation
                    // These are handled by WindowlessDialogInputPatch, so don't process them here
                    // Return immediately to prevent other handlers from interfering
                    return;
                }
            }

            // ===== EARLY CHECK: Skip arrow keys and Enter if Dialog_NodeTree is open =====
            // DialogAccessibilityPatch handles keyboard navigation for Dialog_NodeTree windows
            if (Find.WindowStack != null)
            {
                // Check if any Dialog_NodeTree window is currently open
                foreach (var window in Find.WindowStack.Windows)
                {
                    if (window is Dialog_NodeTree)
                    {
                        // Let arrow keys and Enter pass through to DialogAccessibilityPatch
                        if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                            key == KeyCode.Return || key == KeyCode.KeypadEnter)
                        {
                            Log.Message($"[UnifiedKeyboardPatch] Dialog_NodeTree open, letting key {key} pass through");
                            // Don't consume these keys - let DialogAccessibilityPatch handle them
                            return;
                        }
                        break;
                    }
                }
            }

            // ===== PRIORITY 0: Handle caravan stats viewer if active (must be before key blocking) =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (CaravanStatsState.IsActive && !WindowlessDialogState.IsActive)
            {
                if (CaravanStatsState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0: Handle settlement browser in world view (must be before key blocking) =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (SettlementBrowserState.IsActive && !WindowlessDialogState.IsActive)
            {
                if (SettlementBrowserState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }

                // === Handle Home/End for menu navigation ===
                if (key == KeyCode.Home)
                {
                    SettlementBrowserState.JumpToFirst();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.End)
                {
                    SettlementBrowserState.JumpToLast();
                    Event.current.Use();
                    return;
                }

                // === Handle Backspace for typeahead ===
                if (key == KeyCode.Backspace)
                {
                    SettlementBrowserState.HandleBackspace();
                    Event.current.Use();
                    return;
                }

                // === Consume ALL alphanumeric + * for typeahead ===
                // This MUST be at the end to catch any unhandled characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);

                if (isLetter || isNumber || isStar)
                {
                    if (isStar)
                    {
                        // Reserved for future "expand all at level" in tree views
                        Event.current.Use();
                        return;
                    }
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    SettlementBrowserState.HandleTypeahead(c);
                    Event.current.Use();
                    return;  // CRITICAL: Don't fall through to other handlers
                }
            }

            // ===== PRIORITY 0.25: Handle caravan destination selection if active =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (CaravanFormationState.IsChoosingDestination && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = Event.current.alt;

                // Handle Enter key to set destination
                if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
                {
                    CaravanFormationState.SetDestination(WorldNavigationState.CurrentSelectedTile);
                    Event.current.Use();
                    return;
                }

                // Handle Escape key to cancel destination selection
                if (key == KeyCode.Escape)
                {
                    CaravanFormationState.CancelDestinationSelection();
                    Event.current.Use();
                    return;
                }

                // Let arrow keys pass through for world navigation
            }

            // ===== PRIORITY 0.3: Handle caravan formation dialog if active =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (CaravanFormationState.IsActive && !CaravanFormationState.IsChoosingDestination && !WindowlessDialogState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = Event.current.alt;

                if (CaravanFormationState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.5: Handle world scanner keys (PageUp/PageDown/Home/End) =====
            // Allow these keys when choosing destination, but not when in the formation dialog itself
            // BUT: Skip if caravan stats is active - it handles its own input
            if (WorldNavigationState.IsActive &&
                !CaravanStatsState.IsActive &&
                !QuestMenuState.IsActive &&
                (!CaravanFormationState.IsActive || CaravanFormationState.IsChoosingDestination) &&
                !WindowlessDialogState.IsActive)
            {
                bool handled = false;
                bool alt = Event.current.alt;
                bool ctrl = Event.current.control;
                bool shift = Event.current.shift;

                // Page Down: Next item (Ctrl = next category)
                if (key == KeyCode.PageDown && !shift && !alt)
                {
                    if (ctrl)
                        WorldScannerState.NextCategory();
                    else
                        WorldScannerState.NextItem();
                    handled = true;
                }
                // Page Up: Previous item (Ctrl = previous category)
                else if (key == KeyCode.PageUp && !shift && !alt)
                {
                    if (ctrl)
                        WorldScannerState.PreviousCategory();
                    else
                        WorldScannerState.PreviousItem();
                    handled = true;
                }
                // Home: Jump to scanner item (Alt = home settlement)
                else if (key == KeyCode.Home && !shift && !ctrl)
                {
                    if (alt)
                        WorldNavigationState.JumpToHome();
                    else
                        WorldScannerState.JumpToCurrent();
                    handled = true;
                }
                // End: Read distance/direction (Alt = nearest caravan)
                else if (key == KeyCode.End && !shift && !ctrl)
                {
                    if (alt)
                        WorldNavigationState.JumpToNearestCaravan();
                    else
                        WorldScannerState.ReadDistanceAndDirection();
                    handled = true;
                }
                // Alt+J: Toggle auto-jump mode
                else if (key == KeyCode.J && alt && !shift && !ctrl)
                {
                    WorldScannerState.ToggleAutoJumpMode();
                    handled = true;
                }
                // Comma/Period: Cycle caravans
                else if (key == KeyCode.Period && !shift && !ctrl && !alt)
                {
                    WorldNavigationState.CycleToNextCaravan();
                    handled = true;
                }
                else if (key == KeyCode.Comma && !shift && !ctrl && !alt)
                {
                    WorldNavigationState.CycleToPreviousCaravan();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.6: Handle route planner if active =====
            // Route planner needs to handle Space (add waypoint), Delete (remove), E (ETA), Escape (close)
            // Space must be consumed to prevent pause/unpause
            // Note: Must check ProgramState first - Find.WorldRoutePlanner access crashes on main menu
            if (Current.ProgramState == ProgramState.Playing && RoutePlannerState.IsActive)
            {
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = Event.current.alt;

                if (RoutePlannerState.HandleInput(key, shift, ctrl, alt))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.7: R key to toggle route planner in world view =====
            // Note: Must check ProgramState first for safety
            if (Current.ProgramState == ProgramState.Playing &&
                WorldNavigationState.IsActive &&
                !CaravanFormationState.IsActive &&
                !CaravanStatsState.IsActive &&
                !WindowlessDialogState.IsActive &&
                !RoutePlannerState.IsActive)
            {
                if (key == KeyCode.R && !Event.current.shift && !Event.current.control && !Event.current.alt)
                {
                    RoutePlannerState.Open();
                    Event.current.Use();
                    return;
                }
            }

            // ===== EARLY BLOCK: If in world view, block most map-specific keys =====
            // Don't block when choosing destination (allow map interaction)
            // Don't block Enter/Escape when menus are active (need them for menu navigation)
            if (WorldNavigationState.IsActive &&
                !CaravanFormationState.IsActive &&
                !WindowlessFloatMenuState.IsActive &&
                !QuestMenuState.IsActive &&
                !CaravanStatsState.IsActive)
            {
                // Block all map-specific keys - world scanner handles PageUp/PageDown/Home/End above
                // Note: R is NOT blocked - it opens route planner (handled above)
                if (key == KeyCode.A ||
                    key == KeyCode.G || key == KeyCode.L || key == KeyCode.Q ||
                    key == KeyCode.Return || key == KeyCode.KeypadEnter ||
                    key == KeyCode.P || key == KeyCode.S ||
                    key == KeyCode.F2 || key == KeyCode.F3 || key == KeyCode.F6 || key == KeyCode.F7 ||
                    key == KeyCode.T || key == KeyCode.Tab ||
                    (key == KeyCode.M && Event.current.alt) ||
                    (key == KeyCode.H && Event.current.alt) ||
                    (key == KeyCode.N && Event.current.alt) ||
                    (key == KeyCode.B && Event.current.alt) ||
                    (key == KeyCode.F && Event.current.alt))
                {
                    // These keys should not work in world view - they're map-specific
                    return;
                }
            }

            // Log if area painting is active
            if (AreaPaintingState.IsActive)
            {
                Log.Message($"RimWorld Access: UnifiedKeyboardPatch - Area painting is ACTIVE, key={key}");
            }

            // ===== PRIORITY 1: Handle delete confirmation if active =====
            if (WindowlessDeleteConfirmationState.IsActive)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessDeleteConfirmationState.Confirm();
                    Event.current.Use();
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessDeleteConfirmationState.Cancel();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2: Handle general confirmation if active =====
            if (WindowlessConfirmationState.IsActive)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessConfirmationState.Confirm();
                    Event.current.Use();
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessConfirmationState.Cancel();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.3: Handle trade confirmation if active =====
            if (TradeConfirmationState.IsActive)
            {
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    TradeConfirmationState.Confirm();
                    Event.current.Use();
                    return;
                }
                else if (key == KeyCode.Escape)
                {
                    TradeConfirmationState.Cancel();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.5: Handle area painting mode if active =====
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (AreaPaintingState.IsActive && !WindowlessDialogState.IsActive)
            {
                Log.Message($"RimWorld Access: Area painting active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.Space)
                {
                    Log.Message("RimWorld Access: Space pressed in area painting mode");
                    IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

                    if (!AreaPaintingState.HasRectangleStart)
                    {
                        // No start corner yet - set it
                        AreaPaintingState.SetRectangleStart(currentPosition);
                    }
                    else if (AreaPaintingState.IsInPreviewMode)
                    {
                        // We have a preview - confirm this rectangle
                        AreaPaintingState.ConfirmRectangle();
                    }
                    else
                    {
                        // Start is set but no end yet - update to create preview at current position
                        AreaPaintingState.UpdatePreview(currentPosition);
                        // Then confirm it
                        AreaPaintingState.ConfirmRectangle();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    Log.Message("RimWorld Access: Enter pressed in area painting mode");
                    // If in preview mode, confirm the rectangle first
                    if (AreaPaintingState.IsInPreviewMode)
                    {
                        AreaPaintingState.ConfirmRectangle();
                    }
                    // Then confirm the area painting
                    AreaPaintingState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    Log.Message("RimWorld Access: Escape pressed in area painting mode");
                    if (AreaPaintingState.HasRectangleStart)
                    {
                        // Cancel current rectangle selection
                        AreaPaintingState.CancelRectangle();
                    }
                    else
                    {
                        // No rectangle in progress - cancel entire area painting
                        AreaPaintingState.Cancel();
                    }
                    handled = true;
                }
                // Note: Arrow keys are NOT handled here - they pass through to MapNavigationPatch

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.6: Handle trade menu if active =====
            // (but not if confirmation dialog is showing - that's handled at Priority 2.3)
            // BUT: Skip if windowless dialog is active - dialogs take absolute priority
            if (TradeNavigationState.IsActive && !TradeConfirmationState.IsActive && !WindowlessDialogState.IsActive)
            {
                bool handled = false;

                // Check for modifier keys
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = Event.current.alt;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    TradeNavigationState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    TradeNavigationState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then exit quantity mode, then close
                else if (key == KeyCode.Escape)
                {
                    if (TradeNavigationState.HasActiveSearch)
                    {
                        TradeNavigationState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        // Escape exits quantity mode first, then closes menu
                        bool exitedQuantityMode = TradeNavigationState.ExitQuantityMode();
                        // If we didn't exit quantity mode (were already in list view), close the trade
                        if (!exitedQuantityMode)
                        {
                            TradeNavigationState.Close();
                            TradeSession.Close();
                        }
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && TradeNavigationState.HasActiveSearch)
                {
                    TradeNavigationState.ProcessBackspace();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    if (shift)
                        TradeNavigationState.AdjustQuantityLarge(-1);
                    else if (ctrl)
                        TradeNavigationState.AdjustQuantityVeryLarge(-1);
                    else if (TradeNavigationState.HasActiveSearch && !TradeNavigationState.HasNoMatches)
                        TradeNavigationState.SelectNextMatch();
                    else
                        TradeNavigationState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (shift)
                        TradeNavigationState.AdjustQuantityLarge(1);
                    else if (ctrl)
                        TradeNavigationState.AdjustQuantityVeryLarge(1);
                    else if (alt)
                        TradeNavigationState.SetToMaximumSell();
                    else if (TradeNavigationState.HasActiveSearch && !TradeNavigationState.HasNoMatches)
                        TradeNavigationState.SelectPreviousMatch();
                    else
                        TradeNavigationState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    TradeNavigationState.PreviousCategory();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    TradeNavigationState.NextCategory();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    // Enter either enters quantity mode or exits it
                    TradeNavigationState.EnterQuantityMode();
                    handled = true;
                }
                // Alt+key shortcuts (to not conflict with typeahead)
                else if (alt && key == KeyCode.A)
                {
                    TradeNavigationState.AcceptTrade();
                    handled = true;
                }
                else if (alt && key == KeyCode.R && !shift)
                {
                    TradeNavigationState.ResetCurrentItem();
                    handled = true;
                }
                else if (alt && key == KeyCode.R && shift)
                {
                    TradeNavigationState.ResetAll();
                    handled = true;
                }
                else if (alt && key == KeyCode.G)
                {
                    TradeNavigationState.ToggleGiftMode();
                    handled = true;
                }
                else if (alt && key == KeyCode.P)
                {
                    TradeNavigationState.ShowPriceBreakdown();
                    handled = true;
                }
                else if (alt && key == KeyCode.B)
                {
                    TradeNavigationState.AnnounceTradeBalance();
                    handled = true;
                }
                else if (key == KeyCode.Minus || key == KeyCode.KeypadMinus)
                {
                    TradeNavigationState.AdjustQuantity(-1);
                    handled = true;
                }
                else if (key == KeyCode.Plus || key == KeyCode.KeypadPlus || key == KeyCode.Equals)
                {
                    TradeNavigationState.AdjustQuantity(1);
                    handled = true;
                }
                // Handle typeahead characters (letters and numbers - commands now use Alt+ so no exclusions needed)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !shift && !ctrl && !alt)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        TradeNavigationState.ProcessTypeaheadCharacter(c);
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 3: Handle save/load menu if active =====
            if (WindowlessSaveMenuState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessSaveMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessSaveMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessSaveMenuState.HasActiveSearch)
                    {
                        WindowlessSaveMenuState.ClearTypeaheadSearch();
                    }
                    else
                    {
                        WindowlessSaveMenuState.GoBack();
                    }
                    handled = true;
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessSaveMenuState.HasActiveSearch)
                {
                    WindowlessSaveMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate with search awareness
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessSaveMenuState.SelectNextMatch();
                    handled = true;
                }
                // Handle Up arrow - navigate with search awareness
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessSaveMenuState.SelectPreviousMatch();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessSaveMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Delete)
                {
                    WindowlessSaveMenuState.DeleteSelected();
                    handled = true;
                }
                // Handle typeahead characters
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isLetter || isNumber)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        WindowlessSaveMenuState.ProcessTypeaheadCharacter(c);
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4: Handle pause menu if active =====
            if (WindowlessPauseMenuState.IsActive)
            {
                if (WindowlessPauseMenuState.HandleInput())
                {
                    Event.current.Use();
                    return;
                }

                // HandleInput returns false for Escape without active search - handle closing here
                if (key == KeyCode.Escape)
                {
                    WindowlessPauseMenuState.Close();
                    TolkHelper.Speak("Menu closed");
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.5: Handle storyteller selection (in-game) if active =====
            if (StorytellerSelectionState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    StorytellerSelectionState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    StorytellerSelectionState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Tab)
                {
                    StorytellerSelectionState.SwitchLevel();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    StorytellerSelectionState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    StorytellerSelectionState.Close();
                    Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame));
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.6: Handle options menu if active =====
            if (WindowlessOptionsMenuState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessOptionsMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessOptionsMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then go back
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch)
                    {
                        WindowlessOptionsMenuState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessOptionsMenuState.GoBack();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessOptionsMenuState.HasActiveSearch)
                {
                    WindowlessOptionsMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle Up arrow - navigate with search awareness
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch && !WindowlessOptionsMenuState.HasNoMatches)
                    {
                        WindowlessOptionsMenuState.SelectPreviousMatch();
                    }
                    else
                    {
                        WindowlessOptionsMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                // Handle Down arrow - navigate with search awareness
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessOptionsMenuState.HasActiveSearch && !WindowlessOptionsMenuState.HasNoMatches)
                    {
                        WindowlessOptionsMenuState.SelectNextMatch();
                    }
                    else
                    {
                        WindowlessOptionsMenuState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Left/Right arrows - only for settings level to adjust values
                else if (key == KeyCode.LeftArrow)
                {
                    if (WindowlessOptionsMenuState.CurrentLevel == OptionsMenuLevel.SettingsList)
                    {
                        WindowlessOptionsMenuState.AdjustSetting(-1);  // Decrease slider or cycle left
                        handled = true;
                    }
                }
                else if (key == KeyCode.RightArrow)
                {
                    if (WindowlessOptionsMenuState.CurrentLevel == OptionsMenuLevel.SettingsList)
                    {
                        WindowlessOptionsMenuState.AdjustSetting(1);   // Increase slider or cycle right
                        handled = true;
                    }
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessOptionsMenuState.ExecuteSelected();
                    handled = true;
                }
                // Handle typeahead characters (letter keys)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if ((isLetter || isNumber) && !Event.current.shift && !Event.current.control && !Event.current.alt)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        WindowlessOptionsMenuState.ProcessTypeaheadCharacter(c);
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // Note: ThingFilterMenuState, BillConfigState, BillsMenuState, and BuildingInspectState
            // are all handled by BuildingInspectPatch with VeryHigh priority.
            // We don't need to check for them here because BuildingInspectPatch will consume
            // the events before they reach this patch. However, we DO need to continue processing
            // to handle WindowlessFloatMenuState which can be active at the same time as BillsMenuState.

            // ===== PRIORITY 4.55: Handle schedule menu if active =====
            if (WindowlessScheduleState.IsActive)
            {
                bool handled = false;
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;

                if (key == KeyCode.UpArrow)
                {
                    WindowlessScheduleState.MoveUp();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    WindowlessScheduleState.MoveDown();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessScheduleState.MoveLeft();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    if (shift)
                    {
                        WindowlessScheduleState.FillRow();
                    }
                    else
                    {
                        WindowlessScheduleState.MoveRight();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Tab)
                {
                    if (shift)
                    {
                        WindowlessScheduleState.CycleAssignmentBackward();
                    }
                    else
                    {
                        WindowlessScheduleState.CycleAssignment();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    WindowlessScheduleState.ApplyAssignment();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    // Confirm and close
                    WindowlessScheduleState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    // Cancel and close
                    WindowlessScheduleState.Cancel();
                    handled = true;
                }
                else if (ctrl && key == KeyCode.C)
                {
                    WindowlessScheduleState.CopySchedule();
                    handled = true;
                }
                else if (ctrl && key == KeyCode.V)
                {
                    WindowlessScheduleState.PasteSchedule();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.6: Handle research detail view if active =====
            if (WindowlessResearchDetailState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessResearchDetailState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessResearchDetailState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch)
                    {
                        WindowlessResearchDetailState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessResearchDetailState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessResearchDetailState.HasActiveSearch)
                {
                    WindowlessResearchDetailState.ProcessBackspace();
                    handled = true;
                }
                // Handle Up/Down with typeahead filtering
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch && !WindowlessResearchDetailState.HasNoMatches)
                    {
                        WindowlessResearchDetailState.SelectNextMatch();
                    }
                    else
                    {
                        WindowlessResearchDetailState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessResearchDetailState.HasActiveSearch && !WindowlessResearchDetailState.HasNoMatches)
                    {
                        WindowlessResearchDetailState.SelectPreviousMatch();
                    }
                    else
                    {
                        WindowlessResearchDetailState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessResearchDetailState.Expand();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessResearchDetailState.Collapse();
                    handled = true;
                }
                // Handle * key - expand all sibling categories (WCAG tree view pattern)
                else if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
                {
                    WindowlessResearchDetailState.ExpandAllSiblings();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchDetailState.ExecuteCurrentItem();
                    handled = true;
                }
                // Handle typeahead characters
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isLetter || isNumber)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        WindowlessResearchDetailState.ProcessTypeaheadCharacter(c);
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7: Handle research menu if active =====
            if (WindowlessResearchMenuState.IsActive)
            {
                bool handled = false;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WindowlessResearchMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WindowlessResearchMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch)
                    {
                        WindowlessResearchMenuState.ClearTypeaheadSearch();
                        handled = true;
                    }
                    else
                    {
                        WindowlessResearchMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace && WindowlessResearchMenuState.HasActiveSearch)
                {
                    WindowlessResearchMenuState.ProcessBackspace();
                    handled = true;
                }
                // Handle * key - expand all sibling categories (WCAG tree view pattern)
                else if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
                {
                    WindowlessResearchMenuState.ExpandAllSiblings();
                    handled = true;
                }
                // Handle Up/Down with typeahead filtering (only navigate matches when there ARE matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch && !WindowlessResearchMenuState.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        WindowlessResearchMenuState.SelectNextMatch();
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WindowlessResearchMenuState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessResearchMenuState.HasActiveSearch && !WindowlessResearchMenuState.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        WindowlessResearchMenuState.SelectPreviousMatch();
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WindowlessResearchMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessResearchMenuState.ExpandCategory();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessResearchMenuState.CollapseCategory();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchMenuState.ExecuteSelected();
                    handled = true;
                }
                // Handle typeahead characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                else
                {
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isLetter || isNumber)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        WindowlessResearchMenuState.ProcessTypeaheadCharacter(c);
                        handled = true;
                    }
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.73: Handle quest menu if active =====
            if (QuestMenuState.IsActive)
            {
                bool handled = false;
                bool alt = Event.current.alt;
                var typeahead = QuestMenuState.Typeahead;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    QuestMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    QuestMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        QuestMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        QuestMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    QuestMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(QuestMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            QuestMenuState.SetCurrentIndex(newIndex);
                            QuestMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        QuestMenuState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Up arrow (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(QuestMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            QuestMenuState.SetCurrentIndex(newIndex);
                            QuestMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        QuestMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    QuestMenuState.NextTab();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    QuestMenuState.PreviousTab();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    QuestMenuState.ViewSelectedQuest();
                    handled = true;
                }
                else if (key == KeyCode.A && alt)
                {
                    QuestMenuState.AcceptQuest();
                    handled = true;
                }
                else if (key == KeyCode.D && alt)
                {
                    QuestMenuState.ToggleDismissQuest();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle * key - consume to prevent passthrough
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isStarKey = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
                if (isStarKey)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    QuestMenuState.HandleTypeahead(c);
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.73: Handle wildlife menu if active =====
            if (WildlifeMenuState.IsActive)
            {
                bool handled = false;
                var typeahead = WildlifeMenuState.Typeahead;

                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    WildlifeMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    WildlifeMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        WildlifeMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        WildlifeMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    WildlifeMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(WildlifeMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            WildlifeMenuState.SetCurrentAnimalIndex(newIndex);
                            WildlifeMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WildlifeMenuState.SelectNextAnimal();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(WildlifeMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            WildlifeMenuState.SetCurrentAnimalIndex(newIndex);
                            WildlifeMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        WildlifeMenuState.SelectPreviousAnimal();
                    }
                    handled = true;
                }
                // Handle Right arrow - navigate columns
                else if (key == KeyCode.RightArrow)
                {
                    WildlifeMenuState.SelectNextColumn();
                    handled = true;
                }
                // Handle Left arrow - navigate columns
                else if (key == KeyCode.LeftArrow)
                {
                    WildlifeMenuState.SelectPreviousColumn();
                    handled = true;
                }
                // Handle Enter - interact with current cell
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WildlifeMenuState.InteractWithCurrentCell();
                    handled = true;
                }
                // Handle Alt+S - sort by current column
                else if (key == KeyCode.S && Event.current.alt)
                {
                    WildlifeMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    WildlifeMenuState.HandleTypeahead(c);
                    Event.current.Use();
                    return;
                }

                // Consume other keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.74: Handle animals menu if active =====
            if (AnimalsMenuState.IsActive)
            {
                bool handled = false;
                var typeahead = AnimalsMenuState.Typeahead;

                // Check if in submenu
                if (AnimalsMenuState.IsInSubmenu)
                {
                    var submenuTypeahead = AnimalsMenuState.SubmenuTypeahead;

                    // Handle Escape - clear search FIRST, then close submenu
                    if (key == KeyCode.Escape)
                    {
                        if (submenuTypeahead.HasActiveSearch)
                        {
                            submenuTypeahead.ClearSearchAndAnnounce();
                            AnimalsMenuState.AnnounceSubmenuWithSearch();
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuCancel();
                        }
                        handled = true;
                    }
                    // Handle Backspace for search
                    else if (key == KeyCode.Backspace)
                    {
                        AnimalsMenuState.SubmenuHandleBackspace();
                        handled = true;
                    }
                    // Handle Down arrow (use typeahead if active with matches)
                    else if (key == KeyCode.DownArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetNextMatch(AnimalsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AnimalsMenuState.SetSubmenuSelectedIndex(newIndex);
                                AnimalsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuSelectNext();
                        }
                        handled = true;
                    }
                    // Handle Up arrow (use typeahead if active with matches)
                    else if (key == KeyCode.UpArrow)
                    {
                        if (submenuTypeahead.HasActiveSearch && !submenuTypeahead.HasNoMatches)
                        {
                            int newIndex = submenuTypeahead.GetPreviousMatch(AnimalsMenuState.SubmenuSelectedIndex);
                            if (newIndex >= 0)
                            {
                                AnimalsMenuState.SetSubmenuSelectedIndex(newIndex);
                                AnimalsMenuState.AnnounceSubmenuWithSearch();
                            }
                        }
                        else
                        {
                            AnimalsMenuState.SubmenuSelectPrevious();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                    {
                        AnimalsMenuState.SubmenuApply();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle typeahead characters in submenu
                    bool isSubmenuLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isSubmenuNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isSubmenuLetter || isSubmenuNumber)
                    {
                        char c = isSubmenuLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        AnimalsMenuState.SubmenuHandleTypeahead(c);
                        Event.current.Use();
                        return;
                    }

                    // Consume other keys in submenu
                    Event.current.Use();
                    return;
                }

                // Main menu handling
                // Handle Home - jump to first
                if (key == KeyCode.Home)
                {
                    AnimalsMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last
                else if (key == KeyCode.End)
                {
                    AnimalsMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then close
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AnimalsMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        AnimalsMenuState.Close();
                        handled = true;
                    }
                }
                // Handle Backspace for search
                else if (key == KeyCode.Backspace)
                {
                    AnimalsMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(AnimalsMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            AnimalsMenuState.SetCurrentAnimalIndex(newIndex);
                            AnimalsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        AnimalsMenuState.SelectNextAnimal();
                    }
                    handled = true;
                }
                // Handle Up arrow - navigate animals (use typeahead if active with matches)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(AnimalsMenuState.CurrentAnimalIndex);
                        if (newIndex >= 0)
                        {
                            AnimalsMenuState.SetCurrentAnimalIndex(newIndex);
                            AnimalsMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        AnimalsMenuState.SelectPreviousAnimal();
                    }
                    handled = true;
                }
                // Handle Right arrow - navigate columns
                else if (key == KeyCode.RightArrow)
                {
                    AnimalsMenuState.SelectNextColumn();
                    handled = true;
                }
                // Handle Left arrow - navigate columns
                else if (key == KeyCode.LeftArrow)
                {
                    AnimalsMenuState.SelectPreviousColumn();
                    handled = true;
                }
                // Handle Enter - interact with current cell
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    AnimalsMenuState.InteractWithCurrentCell();
                    handled = true;
                }
                // Handle Alt+S - sort by current column
                else if (key == KeyCode.S && Event.current.alt)
                {
                    AnimalsMenuState.ToggleSortByCurrentColumn();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    AnimalsMenuState.HandleTypeahead(c);
                    Event.current.Use();
                    return;
                }

                // Consume other keys to prevent passthrough
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.75: Handle scanner keys (always available during map navigation) =====
            // Only process scanner keys if in gameplay with map navigation initialized
            // IMPORTANT: Don't process scanner keys when any accessibility menu is active,
            // EXCEPT during placement mode (architect build or designator from gizmos)
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                MapNavigationState.IsInitialized &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                !ZoneCreationState.IsInCreationMode)
            {
                // Check placement mode here (after verifying we're in gameplay)
                bool inPlacementMode = ArchitectState.IsInPlacementMode ||
                    (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null);

                if (!KeyboardHelper.IsAnyAccessibilityMenuActive() || inPlacementMode)
                {
                    bool handled = false;
                    bool ctrl = Event.current.control;
                    bool shift = Event.current.shift;
                    bool alt = Event.current.alt;

                    if (key == KeyCode.PageDown)
                    {
                        if (alt)
                        {
                            ScannerState.NextBulkItem();
                        }
                        else if (ctrl)
                        {
                            ScannerState.NextCategory();
                        }
                        else if (shift)
                        {
                            ScannerState.NextSubcategory();
                        }
                        else
                        {
                            ScannerState.NextItem();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.PageUp)
                    {
                        if (alt)
                        {
                            ScannerState.PreviousBulkItem();
                        }
                        else if (ctrl)
                        {
                            ScannerState.PreviousCategory();
                        }
                        else if (shift)
                        {
                            ScannerState.PreviousSubcategory();
                        }
                        else
                        {
                            ScannerState.PreviousItem();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.Home)
                    {
                        if (alt)
                        {
                            // Alt+Home: Toggle auto-jump mode
                            ScannerState.ToggleAutoJumpMode();
                        }
                        else
                        {
                            // Home: Jump to current item
                            ScannerState.JumpToCurrent();
                        }
                        handled = true;
                    }
                    else if (key == KeyCode.End)
                    {
                        ScannerState.ReadDistanceAndDirection();
                        handled = true;
                    }

                    if (handled)
                    {
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.77: Handle notification menu if active =====
            if (NotificationMenuState.IsActive)
            {
                bool handled = false;
                var typeahead = NotificationMenuState.Typeahead;

                // Handle Home - jump to first (only in list view, not detail view)
                if (key == KeyCode.Home && !NotificationMenuState.IsInDetailView)
                {
                    NotificationMenuState.JumpToFirst();
                    handled = true;
                }
                // Handle End - jump to last (only in list view, not detail view)
                else if (key == KeyCode.End && !NotificationMenuState.IsInDetailView)
                {
                    NotificationMenuState.JumpToLast();
                    handled = true;
                }
                // Handle Escape - clear search FIRST, then go back
                else if (key == KeyCode.Escape)
                {
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        NotificationMenuState.AnnounceWithSearch();
                        handled = true;
                    }
                    else
                    {
                        NotificationMenuState.GoBack();
                        handled = true;
                    }
                }
                // Handle Backspace for search (only in list view)
                else if (key == KeyCode.Backspace && !NotificationMenuState.IsInDetailView)
                {
                    NotificationMenuState.HandleBackspace();
                    handled = true;
                }
                // Handle Down arrow (use typeahead if active with matches, in list view)
                else if (key == KeyCode.DownArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches && !NotificationMenuState.IsInDetailView)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetNextMatch(NotificationMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            NotificationMenuState.SetCurrentIndex(newIndex);
                            NotificationMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        NotificationMenuState.SelectNext();
                    }
                    handled = true;
                }
                // Handle Up arrow (use typeahead if active with matches, in list view)
                else if (key == KeyCode.UpArrow)
                {
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches && !NotificationMenuState.IsInDetailView)
                    {
                        // Navigate through matches only when there ARE matches
                        int newIndex = typeahead.GetPreviousMatch(NotificationMenuState.CurrentIndex);
                        if (newIndex >= 0)
                        {
                            NotificationMenuState.SetCurrentIndex(newIndex);
                            NotificationMenuState.AnnounceWithSearch();
                        }
                    }
                    else
                    {
                        // Navigate normally (either no search active, OR search with no matches)
                        NotificationMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    NotificationMenuState.OpenDetailOrJump();
                    handled = true;
                }
                else if (key == KeyCode.Delete)
                {
                    NotificationMenuState.DeleteSelected();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // Handle typeahead only in list view, not detail view
                if (!NotificationMenuState.IsInDetailView)
                {
                    // Handle * key - consume to prevent passthrough
                    // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                    bool isStarKey = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
                    if (isStarKey)
                    {
                        Event.current.Use();
                        return;
                    }

                    // Handle typeahead characters
                    // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                    bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                    bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                    if (isLetter || isNumber)
                    {
                        char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                        NotificationMenuState.HandleTypeahead(c);
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 4.779: Handle assign menu typeahead if active =====
            if (AssignMenuState.IsActive)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    AssignMenuState.ProcessTypeaheadCharacter(c);
                    Event.current.Use();
                    return;
                }

                if (key == KeyCode.Backspace)
                {
                    AssignMenuState.ProcessBackspace();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7791: Handle storage settings menu typeahead if active =====
            // Note: StorageSettingsMenuPatch handles navigation at higher priority, but letters fall through here
            if (StorageSettingsMenuState.IsActive)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    StorageSettingsMenuState.ProcessTypeaheadCharacter(c);
                    Event.current.Use();
                    return;
                }

                if (key == KeyCode.Backspace)
                {
                    StorageSettingsMenuState.ProcessBackspace();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.7793: Handle plant selection menu typeahead if active =====
            if (PlantSelectionMenuState.IsActive)
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if (isLetter || isNumber)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    PlantSelectionMenuState.HandleTypeahead(c);
                    Event.current.Use();
                    return;
                }

                if (key == KeyCode.Backspace)
                {
                    PlantSelectionMenuState.HandleBackspace();
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.78: Handle gizmo navigation if active =====
            if (GizmoNavigationState.IsActive)
            {
                // Let GizmoNavigationState.HandleInput() process all input
                // It handles typeahead-aware navigation, Home/End, Escape, Enter, etc.
                if (GizmoNavigationState.HandleInput())
                {
                    return;
                }

                // HandleInput returns false for Escape when no search is active
                // Handle close explicitly
                if (key == KeyCode.Escape)
                {
                    GizmoNavigationState.Close();
                    TolkHelper.Speak("Gizmo menu closed");
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.8: Handle inspection menu if active =====
            if (WindowlessInspectionState.IsActive)
            {
                if (WindowlessInspectionState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.805: Handle inventory menu if active =====
            if (WindowlessInventoryState.IsActive)
            {
                if (WindowlessInventoryState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.81: Handle health tab if active =====
            if (HealthTabState.IsActive)
            {
                if (HealthTabState.HandleInput(Event.current))
                {
                    return;
                }
            }

            // ===== PRIORITY 4.85: Handle prisoner tab if active =====
            if (PrisonerTabState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.LeftArrow)
                {
                    PrisonerTabState.PreviousSection();
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    PrisonerTabState.NextSection();
                    handled = true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    PrisonerTabState.NavigateDown();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    PrisonerTabState.NavigateUp();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    PrisonerTabState.ExecuteAction();
                    handled = true;
                }
                else if (key == KeyCode.Space)
                {
                    PrisonerTabState.ToggleCheckbox();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    PrisonerTabState.Close();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 5: Handle order float menu if active =====
            if (WindowlessFloatMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    if (WindowlessFloatMenuState.HasActiveSearch && !WindowlessFloatMenuState.HasNoMatches)
                    {
                        // Navigate through search matches only
                        WindowlessFloatMenuState.HandleInput();
                    }
                    else
                    {
                        WindowlessFloatMenuState.SelectNext();
                    }
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    if (WindowlessFloatMenuState.HasActiveSearch && !WindowlessFloatMenuState.HasNoMatches)
                    {
                        // Navigate through search matches only
                        WindowlessFloatMenuState.HandleInput();
                    }
                    else
                    {
                        WindowlessFloatMenuState.SelectPrevious();
                    }
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessFloatMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    // Clear search first if active, otherwise close the menu
                    if (WindowlessFloatMenuState.ClearTypeaheadSearch())
                    {
                        // Search was cleared, don't close the menu
                        handled = true;
                    }
                    else
                    {
                        // No active search, close the menu
                        WindowlessFloatMenuState.Close();

                        // If architect mode is active (category/tool/material selection), also reset it
                        if (ArchitectState.IsActive && !ArchitectState.IsInPlacementMode)
                        {
                            ArchitectState.Reset();
                        }

                        TolkHelper.Speak("Menu closed");
                        handled = true;
                    }
                }
                // === Handle Home/End for menu navigation ===
                else if (key == KeyCode.Home)
                {
                    WindowlessFloatMenuState.JumpToFirst();
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    WindowlessFloatMenuState.JumpToLast();
                    handled = true;
                }
                // === Handle Backspace for typeahead ===
                else if (key == KeyCode.Backspace)
                {
                    WindowlessFloatMenuState.HandleBackspace();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }

                // === Consume ALL alphanumeric + * for typeahead ===
                // This MUST be at the end to catch any unhandled characters
                // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
                bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);

                if (isLetter || isNumber || isStar)
                {
                    if (isStar)
                    {
                        // Reserved for future "expand all at level" in tree views
                        Event.current.Use();
                        return;
                    }
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    WindowlessFloatMenuState.HandleTypeahead(c);
                    Event.current.Use();
                    return;  // CRITICAL: Don't fall through to T=time, R=draft, etc.
                }
            }

            // ===== PRIORITY 5.45: Handle world map tile info keys 1-5 =====
            if (WorldNavigationState.IsActive &&
                Current.ProgramState == ProgramState.Playing &&
                !Event.current.shift && !Event.current.control && !Event.current.alt)
            {
                int category = 0;
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) category = 1;
                else if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) category = 2;
                else if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) category = 3;
                else if (key == KeyCode.Alpha4 || key == KeyCode.Keypad4) category = 4;
                else if (key == KeyCode.Alpha5 || key == KeyCode.Keypad5) category = 5;

                if (category > 0)
                {
                    WorldNavigationState.AnnounceTileInfoCategory(category);
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 5.5: Handle time control with Shift+1/2/3, intercept 1/2/3 without Shift =====
            if ((key == KeyCode.Alpha1 || key == KeyCode.Keypad1 ||
                 key == KeyCode.Alpha2 || key == KeyCode.Keypad2 ||
                 key == KeyCode.Alpha3 || key == KeyCode.Keypad3) &&
                Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion))
            {
                // Don't intercept if any menu is active (keys 1-5 are used for tile info)
                bool anyMenuActive = WorkMenuState.IsActive ||
                                    ArchitectState.IsActive ||
                                    ZoneCreationState.IsInCreationMode ||
                                    NotificationMenuState.IsActive ||
                                    QuestMenuState.IsActive ||
                                    WindowlessFloatMenuState.IsActive ||
                                    WindowlessPauseMenuState.IsActive ||
                                    WindowlessSaveMenuState.IsActive ||
                                    WindowlessOptionsMenuState.IsActive ||
                                    WindowlessConfirmationState.IsActive ||
                                    StorageSettingsMenuState.IsActive ||
                                    PlantSelectionMenuState.IsActive ||
                                    WindowlessScheduleState.IsActive ||
                                    WindowlessResearchMenuState.IsActive ||
                                    StorytellerSelectionState.IsActive ||
                                    PrisonerTabState.IsActive ||
                                    HealthTabState.IsActive;

                if (!anyMenuActive)
                {
                    // If Shift is held, change time speed
                    if (Event.current.shift)
                    {
                        TimeSpeed newSpeed = TimeSpeed.Normal;

                        if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1)
                            newSpeed = TimeSpeed.Normal;
                        else if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2)
                            newSpeed = TimeSpeed.Fast;
                        else if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3)
                            newSpeed = TimeSpeed.Superfast;

                        // Set the time speed
                        Find.TickManager.CurTimeSpeed = newSpeed;

                        // Play the appropriate sound effect
                        SoundDef soundDef = null;
                        switch (newSpeed)
                        {
                            case TimeSpeed.Paused:
                                soundDef = SoundDefOf.Clock_Stop;
                                break;
                            case TimeSpeed.Normal:
                                soundDef = SoundDefOf.Clock_Normal;
                                break;
                            case TimeSpeed.Fast:
                                soundDef = SoundDefOf.Clock_Fast;
                                break;
                            case TimeSpeed.Superfast:
                                soundDef = SoundDefOf.Clock_Superfast;
                                break;
                            case TimeSpeed.Ultrafast:
                                soundDef = SoundDefOf.Clock_Superfast;
                                break;
                        }
                        soundDef?.PlayOneShotOnCamera();

                        // Note: Announcement is handled by TimeControlAccessibilityPatch
                        // which monitors the CurTimeSpeed setter

                        Event.current.Use();
                        return;
                    }
                    // If Shift is NOT held, consume event to block native time controls
                    // Keys 1-3 are now reserved for tile info (handled by DetailInfoPatch)
                    // DetailInfoPatch uses Input.GetKeyDown() which is separate from Event.current,
                    // so consuming the IMGUI event here won't affect DetailInfoPatch's functionality
                    else
                    {
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 6: Toggle draft mode with R key (if pawn is selected) =====
            if (key == KeyCode.R)
            {
                // Only toggle draft if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. A colonist pawn is selected
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    Find.Selector != null && Find.Selector.NumSelected > 0)
                {
                    // Get the first selected pawn
                    Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;

                    if (selectedPawn != null &&
                        selectedPawn.IsColonist &&
                        selectedPawn.drafter != null &&
                        selectedPawn.drafter.ShowDraftGizmo)
                    {
                        // Toggle draft state
                        bool wasDrafted = selectedPawn.drafter.Drafted;
                        selectedPawn.drafter.Drafted = !wasDrafted;

                        // Announce the change
                        string status = selectedPawn.drafter.Drafted ? "Drafted" : "Undrafted";
                        TolkHelper.Speak($"{selectedPawn.LabelShort} {status}");

                        // Prevent the default R key behavior
                        Event.current.Use();
                        return;
                    }
                }
            }

            // ===== PRIORITY 6.5: Display mood info with Alt+M (if pawn is selected) =====
            if (key == KeyCode.M && Event.current.alt)
            {
                // Only display mood if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display mood information
                    MoodState.DisplayMoodInfo();

                    // Prevent the default M key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.51: Display health info with Alt+H (if pawn is selected) =====
            if (key == KeyCode.H && Event.current.alt)
            {
                // Only display health if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display health information
                    HealthState.DisplayHealthInfo();

                    // Prevent the default H key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.52: Display needs info with Alt+N (if pawn is selected) =====
            if (key == KeyCode.N && Event.current.alt)
            {
                // Only display needs if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display needs information
                    NeedsState.DisplayNeedsInfo();

                    // Prevent the default N key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.525: Display combat log with Alt+B (if pawn is selected) =====
            if (key == KeyCode.B && Event.current.alt)
            {
                // Only display combat log if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Display combat log information
                    CombatLogState.DisplayCombatLog();

                    // Prevent the default B key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.53: Unforbid all items on the map with Alt+F =====
            if (key == KeyCode.F && Event.current.alt)
            {
                // Only unforbid if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Unforbid all items on the map
                    UnforbidAllItems();

                    // Prevent the default F key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.54: Reform caravan with Shift+C (temporary maps only) =====
            if (key == KeyCode.C && Event.current.shift)
            {
                // Only reform caravan if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. On a map (not in world view)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Trigger caravan reformation
                    CaravanFormationState.TriggerReformation();

                    // Prevent the default C key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.55: Announce time, date, and season with T key =====
            if (key == KeyCode.T)
            {
                // Only announce time if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Announce time information
                    TimeAnnouncementState.AnnounceTime();

                    // Prevent the default T key behavior
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6.6: Open windowless schedule menu with F2 key =====
            if (key == KeyCode.F2)
            {
                // Only open schedule if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Schedule menu is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WindowlessScheduleState.IsActive)
                {
                    // Prevent the default S key behavior
                    Event.current.Use();

                    // Open the windowless schedule menu
                    WindowlessScheduleState.Open();

                    return;
                }
            }

            // ===== PRIORITY 6.7: Open assign menu with F3 key =====
            if (key == KeyCode.F3)
            {
                // Only open assign menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default F3 key behavior
                    Event.current.Use();

                    // Get the selected pawn, or use first colonist if none selected
                    Pawn targetPawn = null;
                    if (Find.Selector != null && Find.Selector.NumSelected > 0)
                    {
                        targetPawn = Find.Selector.FirstSelectedObject as Pawn;
                    }

                    // If no pawn selected, use first colonist
                    if (targetPawn == null && Find.CurrentMap.mapPawns.FreeColonists.Any())
                    {
                        targetPawn = Find.CurrentMap.mapPawns.FreeColonists.First();
                    }

                    if (targetPawn != null)
                    {
                        // Open the assign menu
                        AssignMenuState.Open(targetPawn);
                    }
                    else
                    {
                        TolkHelper.Speak("No colonists available");
                    }

                    return;
                }
            }

            // J key is no longer used - scanner is always available via Page Up/Down keys

            // ===== PRIORITY 7.05: Open gizmo navigation with G key (if pawn or building is selected) =====
            if (key == KeyCode.G)
            {
                // Only open gizmo navigation if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Map navigation is initialized (cursor position is valid)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    MapNavigationState.IsInitialized)
                {
                    // Prevent the default G key behavior
                    Event.current.Use();

                    // Decide whether to open gizmos for selected objects or for objects at cursor
                    // Use selected pawn gizmos ONLY if a pawn was just selected with , or .
                    // Otherwise, use objects at the cursor position
                    if (GizmoNavigationState.PawnJustSelected && Find.Selector != null && Find.Selector.NumSelected > 0)
                    {
                        // Open gizmos for the pawn that was just selected with , or .
                        GizmoNavigationState.Open();
                    }
                    else
                    {
                        // Open gizmos for objects at the cursor position
                        IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                        GizmoNavigationState.OpenAtCursor(cursorPosition, Find.CurrentMap);
                    }
                    return;
                }
            }

            // ===== PRIORITY 7.1: Open notification menu with L key (if no menu is active and we're in-game) =====
            if (key == KeyCode.L)
            {
                // Only open notification menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default L key behavior
                    Event.current.Use();

                    // Open the notification menu
                    NotificationMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.5: Open quest menu with F7 key (if no menu is active and we're in-game) =====
            if (key == KeyCode.F7)
            {
                // Only open quest menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default F7 key behavior
                    Event.current.Use();

                    // Open the quest menu
                    QuestMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.55: Open research menu with F6 key (if no menu is active and we're in-game) =====
            if (key == KeyCode.F6)
            {
                // Only open research menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    // Prevent the default F6 key behavior
                    Event.current.Use();

                    // Open the research menu
                    WindowlessResearchMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.6: Open inspection menu with lowercase 'i' key (DISABLED - replaced by inventory menu) =====
            if (key == KeyCode.None) // Changed from KeyCode.I to disable this binding
            {
                // Only open inspection menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Map navigation is initialized
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    MapNavigationState.IsInitialized)
                {
                    // Prevent the default I key behavior
                    Event.current.Use();

                    // Open the inspection menu at the current cursor position
                    WindowlessInspectionState.Open(MapNavigationState.CurrentCursorPosition);
                    return;
                }
            }

            // ===== PRIORITY 7.6b: Open colony inventory menu with uppercase 'I' key =====
            if (key == KeyCode.I)
            {
                // Only open inventory menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. Current map exists
                // 3. No windows are preventing camera motion (means a dialog is open)
                // 4. Not in zone creation mode
                // 5. Inventory menu is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !WindowlessInventoryState.IsActive)
                {
                    // Prevent the default I key behavior
                    Event.current.Use();

                    // Open the colony-wide inventory menu
                    WindowlessInventoryState.Open();
                    return;
                }
            }

            // ===== PRIORITY 7.7: Open prisoner tab with P key (if prisoner/slave is selected) =====
            if (key == KeyCode.P)
            {
                // Only open prisoner tab if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. Prisoner tab is not already active
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !PrisonerTabState.IsActive)
                {
                    // Check if a prisoner or slave is currently visible in the prisoner tab
                    Pawn prisoner = PrisonerTabPatch.GetCurrentPrisoner();
                    if (prisoner != null)
                    {
                        // Prevent the default P key behavior
                        Event.current.Use();

                        // Open the prisoner tab
                        PrisonerTabState.Open(prisoner);
                        return;
                    }
                }
            }

            // ===== PRIORITY 8: Open pause menu with Escape (if no menu is active and we're in-game) =====
            if (key == KeyCode.Escape)
            {
                // Only open pause menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                // 4. No accessibility menus are active (they handle their own Escape)
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode &&
                    !KeyboardHelper.IsAnyAccessibilityMenuActive() &&
                    !QuestLocationsBrowserState.IsActive &&
                    !SettlementBrowserState.IsActive &&
                    !CaravanStatsState.IsActive)
                {
                    // Prevent the default escape behavior (opening game's pause menu)
                    Event.current.Use();

                    // Open our windowless pause menu
                    WindowlessPauseMenuState.Open();
                    return;
                }
            }

            // ===== PRIORITY 9: Handle Enter key for inspection menu =====
            // Don't process if in zone creation mode
            if (ZoneCreationState.IsInCreationMode)
                return;

            // Handle Enter key for opening the inspection menu (same as I key)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // Only process during normal gameplay with a valid map
                if (Find.CurrentMap == null)
                    return;

                // Don't process if any dialog or window that prevents camera motion is open
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;

                // IMPORTANT: Don't intercept Enter if targeting mode is active
                // This allows the targeting system to handle target selection
                if (Find.Targeter != null && Find.Targeter.IsTargeting)
                    return;

                // Check if map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return;

                // Get the cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(Find.CurrentMap))
                {
                    TolkHelper.Speak("Invalid position");
                    Event.current.Use();
                    return;
                }

                // Prevent the default Enter behavior
                Event.current.Use();

                // Open the windowless inspection menu at the current cursor position
                // This is the same menu that opens with the I key
                WindowlessInspectionState.Open(cursorPosition);
                return;
            }

            // ===== PRIORITY 10: Handle right bracket ] key for colonist orders =====
            if (key == KeyCode.RightBracket)
            {
                // Don't process if in world view - WorldNavigationPatch handles ] there
                if (Find.World?.renderer?.wantedMode == RimWorld.Planet.WorldRenderMode.Planet)
                    return;

                // Only process during normal gameplay with a valid map
                if (Find.CurrentMap == null)
                    return;

                // Don't process if any dialog or window that prevents camera motion is open
                if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                    return;

                // Check if map navigation is initialized
                if (!MapNavigationState.IsInitialized)
                    return;

                // Get the cursor position
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                // Validate cursor position
                if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
                {
                    TolkHelper.Speak("Invalid position");
                    Event.current.Use();
                    return;
                }

                // Check for pawns to give orders to
                if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
                {
                    TolkHelper.Speak("No pawn selected");
                    Event.current.Use();
                    return;
                }

                // Get selected pawns
                List<Pawn> selectedPawns = Find.Selector.SelectedPawns.ToList();

                // Get all available actions for this position using RimWorld's built-in system
                Vector3 clickPos = cursorPosition.ToVector3Shifted();
                List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(
                    selectedPawns,
                    clickPos,
                    out FloatMenuContext context
                );

                if (options != null && options.Count > 0)
                {
                    // Open the windowless menu with these options
                    WindowlessFloatMenuState.Open(options, true); // true = gives colonist orders
                }
                else
                {
                    TolkHelper.Speak("No available actions");
                }

                // Consume the event
                Event.current.Use();
            }

            // CATCH-ALL: If any accessibility menu is active, consume ALL remaining key events
            // This prevents ANY keys from leaking to the game when a menu has focus
            if (KeyboardHelper.IsAnyAccessibilityMenuActive() && Event.current.isKey)
            {
                Event.current.Use();
                return;
            }
        }

        /// <summary>
        /// Postfix patch that draws visual overlays for active windowless menus.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Draw schedule menu overlay if active
            if (WindowlessScheduleState.IsActive)
            {
                DrawScheduleMenuOverlay();
            }
        }

        /// <summary>
        /// Draws the visual overlay for the windowless schedule menu.
        /// </summary>
        private static void DrawScheduleMenuOverlay()
        {
            if (WindowlessScheduleState.Pawns.Count == 0)
                return;

            if (WindowlessScheduleState.SelectedPawnIndex < 0 ||
                WindowlessScheduleState.SelectedPawnIndex >= WindowlessScheduleState.Pawns.Count)
                return;

            Pawn selectedPawn = WindowlessScheduleState.Pawns[WindowlessScheduleState.SelectedPawnIndex];
            if (selectedPawn?.timetable == null)
                return;

            int hour = WindowlessScheduleState.SelectedHourIndex;
            TimeAssignmentDef currentAssignment = selectedPawn.timetable.GetAssignment(hour);

            // Get screen dimensions
            float screenWidth = UI.screenWidth;
            float screenHeight = UI.screenHeight;

            // Create overlay rect (top-center of screen)
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

            int pawnNum = WindowlessScheduleState.SelectedPawnIndex + 1;
            int totalPawns = WindowlessScheduleState.Pawns.Count;
            string title = $"Schedule Menu - {selectedPawn.LabelShort} ({pawnNum}/{totalPawns}) - Hour {hour}";
            string currentInfo = $"Current: {currentAssignment.label}";
            string instructions1 = "Arrows: Navigate | Tab: Change Cell | Space: Apply Selected";
            string instructions2 = "Shift+Right: Fill Row | Ctrl+C/V: Copy/Paste | Enter: Save | Esc: Cancel";

            Rect titleRect = new Rect(overlayX, overlayY + 10f, overlayWidth, 30f);
            Rect infoRect = new Rect(overlayX, overlayY + 40f, overlayWidth, 25f);
            Rect instructions1Rect = new Rect(overlayX, overlayY + 70f, overlayWidth, 25f);
            Rect instructions2Rect = new Rect(overlayX, overlayY + 100f, overlayWidth, 25f);

            Widgets.Label(titleRect, title);
            Widgets.Label(infoRect, currentInfo);

            Text.Font = GameFont.Tiny;
            Widgets.Label(instructions1Rect, instructions1);
            Widgets.Label(instructions2Rect, instructions2);

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// Gets the label of the currently selected assignment type.
        /// </summary>
        private static string GetSelectedAssignmentLabel()
        {
            if (WindowlessScheduleState.SelectedAssignment != null)
            {
                return WindowlessScheduleState.SelectedAssignment.label;
            }
            return "Unknown";
        }

        /// <summary>
        /// Unforbids all forbidden items on the current map.
        /// </summary>
        private static void UnforbidAllItems()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("No map available");
                return;
            }

            // Get all things on the map
            List<Thing> allThings = map.listerThings.AllThings;
            int unforbiddenCount = 0;

            // Iterate through all things and unforbid items
            foreach (Thing thing in allThings)
            {
                // Check if the thing can be forbidden (has CompForbiddable component)
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();

                // If it has the component and is currently forbidden, unforbid it
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    thing.SetForbidden(false, warnOnFail: false);
                    unforbiddenCount++;
                }
            }

            // Announce result to user
            if (unforbiddenCount == 0)
            {
                TolkHelper.Speak("No forbidden items found on the map");
            }
            else if (unforbiddenCount == 1)
            {
                TolkHelper.Speak("1 item unforbidden");
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak($"{unforbiddenCount} items unforbidden");
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            Log.Message($"Unforbid all: {unforbiddenCount} items unforbidden");
        }

}
}
