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
    /// Handles: Escape key for pause menu, Enter key for building inspection/beds, ] key for colonist orders, I key for inspection menu, J key for scanner, L key for notification menu, F7 key for quest menu, Alt+M for mood info, Alt+H for health info, Alt+N for needs info, Alt+F for unforbid all items, F2 for schedule, F3 for assign, F6 for research, and all windowless menu navigation.
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

            // ===== PRIORITY 0: Handle settlement browser in world view (must be before key blocking) =====
            if (SettlementBrowserState.IsActive)
            {
                if (SettlementBrowserState.HandleInput(key))
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 0.5: Handle world navigation special keys (Home/End/PageUp/PageDown) =====
            if (WorldNavigationState.IsActive && !SettlementBrowserState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.Home)
                {
                    WorldNavigationState.JumpToHome();
                    handled = true;
                }
                else if (key == KeyCode.End)
                {
                    WorldNavigationState.JumpToNearestCaravan();
                    handled = true;
                }
                else if (key == KeyCode.PageDown)
                {
                    WorldNavigationState.CycleToNextSettlement();
                    handled = true;
                }
                else if (key == KeyCode.PageUp)
                {
                    WorldNavigationState.CycleToPreviousSettlement();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== EARLY BLOCK: If in world view, block most map-specific keys =====
            if (WorldNavigationState.IsActive)
            {
                // Block all map-specific keys (settlement browser is handled in WorldNavigationPatch with High priority)
                if (key == KeyCode.I || key == KeyCode.A || key == KeyCode.Z ||
                    key == KeyCode.G || key == KeyCode.L || key == KeyCode.Q ||
                    key == KeyCode.Return || key == KeyCode.KeypadEnter ||
                    key == KeyCode.RightBracket || key == KeyCode.P ||
                    key == KeyCode.F2 || key == KeyCode.F3 || key == KeyCode.F6 || key == KeyCode.F7 ||
                    key == KeyCode.PageUp || key == KeyCode.PageDown ||
                    key == KeyCode.R || key == KeyCode.T || key == KeyCode.Tab ||
                    (key == KeyCode.M && Event.current.alt) ||
                    (key == KeyCode.H && Event.current.alt) ||
                    (key == KeyCode.N && Event.current.alt) ||
                    (key == KeyCode.F && Event.current.alt))
                {
                    // These keys should not work in world view - they're map-specific
                    // World navigation keys (arrows, home, end, S, I for tile info, Tab in settlement browser)
                    // are all handled by WorldNavigationPatch which runs with High priority before this patch
                    return;
                }
            }

            // Log if area painting is active
            if (AreaPaintingState.IsActive)
            {
                MelonLoader.MelonLogger.Msg($"RimWorld Access: UnifiedKeyboardPatch - Area painting is ACTIVE, key={key}");
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
            if (AreaPaintingState.IsActive)
            {
                MelonLoader.MelonLogger.Msg($"RimWorld Access: Area painting active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.Space)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Space pressed in area painting mode");
                    AreaPaintingState.ToggleStageCell();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Enter pressed in area painting mode");
                    AreaPaintingState.Confirm();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    MelonLoader.MelonLogger.Msg("RimWorld Access: Escape pressed in area painting mode");
                    AreaPaintingState.Cancel();
                    handled = true;
                }
                // Note: Arrow keys are NOT handled here - they pass through to MapNavigationPatch
                // The ZoneCreationAnnouncementPatch postfix will add "Selected" prefix if needed

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 2.6: Handle trade menu if active =====
            // (but not if confirmation dialog is showing - that's handled at Priority 2.3)
            if (TradeNavigationState.IsActive && !TradeConfirmationState.IsActive)
            {
                bool handled = false;

                // Check for modifier keys
                bool shift = Event.current.shift;
                bool ctrl = Event.current.control;
                bool alt = Event.current.alt;

                if (key == KeyCode.DownArrow)
                {
                    if (shift)
                        TradeNavigationState.AdjustQuantityLarge(-1);
                    else if (ctrl)
                        TradeNavigationState.AdjustQuantityVeryLarge(-1);
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
                else if (key == KeyCode.Escape)
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
                else if (key == KeyCode.A && !shift && !ctrl && !alt)
                {
                    TradeNavigationState.AcceptTrade();
                    handled = true;
                }
                else if (key == KeyCode.R && !shift && !ctrl && !alt)
                {
                    TradeNavigationState.ResetCurrentItem();
                    handled = true;
                }
                else if (key == KeyCode.R && shift)
                {
                    TradeNavigationState.ResetAll();
                    handled = true;
                }
                else if (key == KeyCode.G && !shift && !ctrl && !alt)
                {
                    TradeNavigationState.ToggleGiftMode();
                    handled = true;
                }
                else if (key == KeyCode.P && !shift && !ctrl && !alt)
                {
                    TradeNavigationState.ShowPriceBreakdown();
                    handled = true;
                }
                else if (key == KeyCode.B && !shift && !ctrl && !alt)
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

                if (key == KeyCode.DownArrow)
                {
                    WindowlessSaveMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessSaveMenuState.SelectPrevious();
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
                else if (key == KeyCode.Escape)
                {
                    WindowlessSaveMenuState.GoBack();
                    handled = true;
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
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    WindowlessPauseMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessPauseMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessPauseMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessPauseMenuState.Close();
                    TolkHelper.Speak("Menu closed");
                    handled = true;
                }

                if (handled)
                {
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

                if (key == KeyCode.DownArrow)
                {
                    WindowlessOptionsMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessOptionsMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.LeftArrow)
                {
                    WindowlessOptionsMenuState.AdjustSetting(-1);  // Decrease slider or cycle left
                    handled = true;
                }
                else if (key == KeyCode.RightArrow)
                {
                    WindowlessOptionsMenuState.AdjustSetting(1);   // Increase slider or cycle right
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessOptionsMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessOptionsMenuState.GoBack();
                    handled = true;
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
                    WindowlessScheduleState.CycleAssignment();
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

                if (key == KeyCode.DownArrow)
                {
                    WindowlessResearchDetailState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessResearchDetailState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessResearchDetailState.ExecuteCurrentSection();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessResearchDetailState.Close();
                    handled = true;
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

                if (key == KeyCode.DownArrow)
                {
                    WindowlessResearchMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessResearchMenuState.SelectPrevious();
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
                else if (key == KeyCode.Escape)
                {
                    WindowlessResearchMenuState.Close();
                    handled = true;
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

                if (key == KeyCode.DownArrow)
                {
                    QuestMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    QuestMenuState.SelectPrevious();
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
                else if (key == KeyCode.A)
                {
                    QuestMenuState.AcceptQuest();
                    handled = true;
                }
                else if (key == KeyCode.D)
                {
                    QuestMenuState.ToggleDismissQuest();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    QuestMenuState.Close();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.74: Handle animals menu if active =====
            if (AnimalsMenuState.IsActive)
            {
                AnimalsMenuState.HandleInput();
                // AnimalsMenuState.HandleInput() already consumes events internally
                Event.current.Use();
                return;
            }

            // ===== PRIORITY 4.75: Handle scanner keys (always available during map navigation) =====
            // Only process scanner keys if in gameplay with map navigation initialized
            if (Current.ProgramState == ProgramState.Playing &&
                Find.CurrentMap != null &&
                MapNavigationState.IsInitialized &&
                (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                !ZoneCreationState.IsInCreationMode)
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
                    ScannerState.JumpToCurrent();
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

            // ===== PRIORITY 4.77: Handle notification menu if active =====
            if (NotificationMenuState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    NotificationMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    NotificationMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    NotificationMenuState.OpenDetailOrJump();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    NotificationMenuState.GoBack();
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 4.78: Handle gizmo navigation if active =====
            if (GizmoNavigationState.IsActive)
            {
                bool handled = false;

                if (key == KeyCode.DownArrow || key == KeyCode.RightArrow)
                {
                    GizmoNavigationState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow || key == KeyCode.LeftArrow)
                {
                    GizmoNavigationState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    GizmoNavigationState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    GizmoNavigationState.Close();
                    TolkHelper.Speak("Gizmo menu closed");
                    handled = true;
                }

                if (handled)
                {
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
                    WindowlessFloatMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    WindowlessFloatMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    WindowlessFloatMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    WindowlessFloatMenuState.Close();
                    TolkHelper.Speak("Menu closed");
                    handled = true;
                }

                if (handled)
                {
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
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
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

            MelonLoader.MelonLogger.Msg($"Unforbid all: {unforbiddenCount} items unforbidden");
        }

}
}
