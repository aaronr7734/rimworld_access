using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle keyboard input for building-related menus.
    /// Intercepts keyboard events when BillsMenuState, BillConfigState, ThingFilterMenuState, TempControlMenuState, or BedAssignmentState is active.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class BuildingInspectPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryHigh)] // Run before other patches
        public static void Prefix()
        {
            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Handle WindowlessFloatMenuState (highest priority - used for recipe selection and other submenus)
            if (WindowlessFloatMenuState.IsActive)
            {
                // This is handled in UnifiedKeyboardPatch, so just return
                return;
            }

            // Handle InfoCardState - let UnifiedKeyboardPatch handle it at higher priority
            // This ensures info cards opened from menus get proper input handling
            if (InfoCardState.IsActive)
            {
                return;
            }

            // Handle TempControlMenuState (high priority - it's a building settings menu)
            if (TempControlMenuState.IsActive)
            {
                HandleTempControlInput();
                return;
            }

            // Handle BedAssignmentState (high priority - it's a building settings menu)
            if (BedAssignmentState.IsActive)
            {
                HandleBedAssignmentInput();
                return;
            }
            // Handle BuildingOwnerAssignmentState (generic owner assignment menu)
            if (BuildingOwnerAssignmentState.IsActive)
            {
                HandleBuildingOwnerAssignmentInput();
                return;
            }
            // Handle FlickableComponentState (building component menu)
            if (FlickableComponentState.IsActive)
            {
                HandleFlickableComponentInput();
                return;
            }

            // Handle RefuelableComponentState (building component menu)
            if (RefuelableComponentState.IsActive)
            {
                HandleRefuelableComponentInput();
                return;
            }

            // Handle BreakdownableComponentState (building component menu)
            if (BreakdownableComponentState.IsActive)
            {
                HandleBreakdownableComponentInput();
                return;
            }
            // Handle DoorControlState (building-specific menu)
            if (DoorControlState.IsActive)
            {
                HandleDoorControlInput();
                return;
            }
            // Handle ForbidControlState (building component menu)
            if (ForbidControlState.IsActive)
            {
                HandleForbidControlInput();
                return;
            }

            // Handle FishingZoneMenuState (zone settings menu - Odyssey DLC)
            if (FishingZoneMenuState.IsActive)
            {
                HandleFishingZoneMenuInput();
                return;
            }


            // Handle ThingFilterMenuState (second highest priority - it's a submenu)
            if (ThingFilterMenuState.IsActive)
            {
                HandleThingFilterInput();
                return;
            }

            // Handle BillConfigState (third priority)
            if (BillConfigState.IsActive)
            {
                HandleBillConfigInput();
                return;
            }

            // Handle BillsMenuState (fourth priority)
            if (BillsMenuState.IsActive)
            {
                HandleBillsMenuInput();
                return;
            }
        }

        private static void HandleTempControlInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    TempControlMenuState.IncreaseTemperatureSmall();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    TempControlMenuState.DecreaseTemperatureSmall();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    TempControlMenuState.IncreaseTemperatureLarge();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                    TempControlMenuState.DecreaseTemperatureLarge();
                    Event.current.Use();
                    break;

                case KeyCode.R:
                    TempControlMenuState.ResetTemperature();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    TempControlMenuState.Close();
                    TolkHelper.Speak("Closed temperature control menu");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBillsMenuInput()
        {
            KeyCode key = Event.current.keyCode;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                BillsMenuState.JumpToFirst();
                Event.current.Use();
                return;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                BillsMenuState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (BillsMenuState.HasActiveSearch)
                {
                    BillsMenuState.ClearTypeaheadSearch();
                    BillsMenuState.AnnounceWithSearch();
                    Event.current.Use();
                    return;
                }
                BillsMenuState.Close();
                TolkHelper.Speak("Closed bills menu");
                Event.current.Use();
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && BillsMenuState.HasActiveSearch)
            {
                BillsMenuState.ProcessBackspace();
                Event.current.Use();
                return;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            // Exclude keys used for other purposes: C for copy (with Ctrl)
            bool isExcludedLetter = key == KeyCode.C && Event.current.control;

            if ((isLetter || isNumber) && !isExcludedLetter && !Event.current.alt)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                if (!BillsMenuState.ProcessTypeaheadCharacter(c))
                {
                    TolkHelper.Speak($"No matches for '{BillsMenuState.GetLastFailedSearch()}'");
                }
                Event.current.Use();
                return;
            }

            // Handle Alt+I for info card
            if (Event.current.alt && key == KeyCode.I)
            {
                BillsMenuState.OpenInfoCard();
                Event.current.Use();
                return;
            }

            // Handle Ctrl+Arrow for reordering bills
            if (Event.current.control)
            {
                if (key == KeyCode.UpArrow)
                {
                    BillsMenuState.MoveUp();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.DownArrow)
                {
                    BillsMenuState.MoveDown();
                    Event.current.Use();
                    return;
                }
            }

            // Handle Arrow Up - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (BillsMenuState.HasActiveSearch && !BillsMenuState.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int newIndex = BillsMenuState.SelectPreviousMatch();
                    if (newIndex >= 0)
                    {
                        BillsMenuState.SetSelectedIndex(newIndex);
                        BillsMenuState.AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    BillsMenuState.SelectPrevious();
                }
                Event.current.Use();
                return;
            }

            // Handle Arrow Down - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (BillsMenuState.HasActiveSearch && !BillsMenuState.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int newIndex = BillsMenuState.SelectNextMatch();
                    if (newIndex >= 0)
                    {
                        BillsMenuState.SetSelectedIndex(newIndex);
                        BillsMenuState.AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    BillsMenuState.SelectNext();
                }
                Event.current.Use();
                return;
            }

            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    BillsMenuState.ExecuteSelected();
                    Event.current.Use();
                    break;

                case KeyCode.Delete:
                    BillsMenuState.DeleteSelected();
                    Event.current.Use();
                    break;

                case KeyCode.C:
                    if (Event.current.control)
                    {
                        BillsMenuState.CopySelected();
                        Event.current.Use();
                    }
                    break;
            }
        }

        private static void HandleBillConfigInput()
        {
            // Handle range edit submenu first (HP range, quality range)
            if (RangeEditMenuState.IsActive)
            {
                HandleBillConfigRangeEditInput();
                return;
            }

            // Handle text input mode (bill rename)
            if (BillConfigState.IsTextInputMode)
            {
                HandleBillRenameInput();
                return;
            }

            KeyCode key = Event.current.keyCode;

            // Handle numeric input mode
            if (BillConfigState.IsNumericInputMode)
            {
                if (key == KeyCode.Escape)
                {
                    BillConfigState.CancelNumericInput();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    BillConfigState.ConfirmNumericInput();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Backspace)
                {
                    BillConfigState.HandleNumericBackspace();
                    Event.current.Use();
                    return;
                }
                // Handle digit keys using keyCode (more reliable than character)
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                {
                    char c = (char)('0' + (key - KeyCode.Alpha0));
                    BillConfigState.HandleNumericDigit(c);
                    Event.current.Use();
                    return;
                }
                if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                {
                    char c = (char)('0' + (key - KeyCode.Keypad0));
                    BillConfigState.HandleNumericDigit(c);
                    Event.current.Use();
                    return;
                }
                // Ignore other keys in numeric mode
                return;
            }

            // Handle Alt+I for info card
            if (Event.current.alt && key == KeyCode.I)
            {
                BillConfigState.OpenInfoCard();
                Event.current.Use();
                return;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (BillConfigState.HasActiveSearch)
                {
                    BillConfigState.ClearTypeaheadSearch();
                    BillConfigState.AnnounceWithSearch();
                    Event.current.Use();
                    return;
                }
                BillConfigState.Close();
                TolkHelper.Speak("Closed bill configuration");
                // Note: BillsMenuState stays active in background, no need to reopen
                Event.current.Use();
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && BillConfigState.HasActiveSearch)
            {
                BillConfigState.ProcessBackspace();
                Event.current.Use();
                return;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !Event.current.alt)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                if (!BillConfigState.ProcessTypeaheadCharacter(c))
                {
                    TolkHelper.Speak($"No matches for '{BillConfigState.GetLastFailedSearch()}'");
                }
                Event.current.Use();
                return;
            }

            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;

            // Modifier key adjustments
            if (key == KeyCode.UpArrow && shift && !ctrl)
            {
                BillConfigState.AdjustValue(1, 10);  // Shift+Up = +10
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && shift && !ctrl)
            {
                BillConfigState.AdjustValue(-1, 10); // Shift+Down = -10
                Event.current.Use();
                return;
            }
            if (key == KeyCode.UpArrow && ctrl && !shift)
            {
                BillConfigState.AdjustValue(1, 100); // Ctrl+Up = +100
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && ctrl && !shift)
            {
                BillConfigState.AdjustValue(-1, 100); // Ctrl+Down = -100
                Event.current.Use();
                return;
            }
            if (key == KeyCode.UpArrow && shift && ctrl)
            {
                BillConfigState.AdjustValue(1, 1000); // Shift+Ctrl+Up = +1000
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && shift && ctrl)
            {
                BillConfigState.AdjustValue(-1, 1000); // Shift+Ctrl+Down = -1000
                Event.current.Use();
                return;
            }

            // Min/Max jumps
            if (key == KeyCode.Home && shift)
            {
                BillConfigState.JumpToMin();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End && shift)
            {
                BillConfigState.JumpToMax();
                Event.current.Use();
                return;
            }

            // Handle Home - jump to first item
            if (key == KeyCode.Home && !shift)
            {
                BillConfigState.JumpToFirst();
                Event.current.Use();
                return;
            }

            // Handle End - jump to last item
            if (key == KeyCode.End && !shift)
            {
                BillConfigState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Handle Arrow Up - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (BillConfigState.HasActiveSearch && !BillConfigState.HasNoMatches)
                {
                    int newIndex = BillConfigState.SelectPreviousMatch();
                    if (newIndex >= 0)
                    {
                        BillConfigState.SetSelectedIndex(newIndex);
                        BillConfigState.AnnounceWithSearch();
                    }
                }
                else
                {
                    BillConfigState.SelectPrevious();
                }
                Event.current.Use();
                return;
            }

            // Handle Arrow Down - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (BillConfigState.HasActiveSearch && !BillConfigState.HasNoMatches)
                {
                    int newIndex = BillConfigState.SelectNextMatch();
                    if (newIndex >= 0)
                    {
                        BillConfigState.SetSelectedIndex(newIndex);
                        BillConfigState.AnnounceWithSearch();
                    }
                }
                else
                {
                    BillConfigState.SelectNext();
                }
                Event.current.Use();
                return;
            }

            switch (key)
            {
                case KeyCode.LeftArrow:
                    BillConfigState.AdjustValue(-1);
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    BillConfigState.AdjustValue(1);
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    BillConfigState.StartNumericInput();
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBillConfigRangeEditInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    RangeEditMenuState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    RangeEditMenuState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                    RangeEditMenuState.DecreaseValue();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    RangeEditMenuState.IncreaseValue();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (RangeEditMenuState.ApplyAndClose(out var hitPoints, out var quality))
                    {
                        BillConfigState.ApplyRangeChanges(hitPoints, quality);
                    }
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    RangeEditMenuState.Close();
                    TolkHelper.Speak("Cancelled range editing");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBillRenameInput()
        {
            KeyCode key = Event.current.keyCode;

            if (key == KeyCode.Escape)
            {
                BillConfigState.CancelTextInput();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                BillConfigState.ConfirmTextInput();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Backspace)
            {
                TextInputHelper.HandleBackspace();
                Event.current.Use();
                return;
            }
            // Handle Ctrl+V paste
            if (Event.current.control && key == KeyCode.V)
            {
                TextInputHelper.HandlePaste();
                Event.current.Use();
                return;
            }
            // Handle character input
            char c = Event.current.character;
            if (c != '\0' && !char.IsControl(c))
            {
                TextInputHelper.HandleCharacter(c);
                Event.current.Use();
            }
        }

        private static void HandleThingFilterInput()
        {
            // Check if range edit submenu is active
            if (RangeEditMenuState.IsActive)
            {
                HandleRangeEditInput();
                return;
            }

            KeyCode key = Event.current.keyCode;

            // Asterisk (*) - expand all sibling categories (WCAG tree view pattern)
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                ThingFilterMenuState.ExpandAllSiblings();
                Event.current.Use();
                return;
            }

            // Handle typeahead character input BEFORE the switch on keyCode.
            // Unity IMGUI sends two KeyDown events per key press:
            //   1. keyCode = KeyCode.T, character = '\0'
            //   2. keyCode = KeyCode.None, character = 't'
            // Checking Event.current.character (the old approach) only catches event 2,
            // letting event 1 leak to UnifiedKeyboardPatch where it triggers game shortcuts.
            // Checking keyCode ranges (like HandleBillsMenuInput does) catches event 1.
            // Exclude shift+number (e.g., Shift+8 = *) to avoid eating modifier combos.
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || (isNumber && !Event.current.shift)) && !Event.current.alt && !Event.current.control)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                ThingFilterMenuState.ProcessTypeaheadCharacter(c);
                Event.current.Use();
                return;
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    if (ThingFilterMenuState.HasActiveSearch)
                        ThingFilterMenuState.SelectPreviousMatch();
                    else
                        ThingFilterMenuState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    if (ThingFilterMenuState.HasActiveSearch)
                        ThingFilterMenuState.SelectNextMatch();
                    else
                        ThingFilterMenuState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    ThingFilterMenuState.ExpandOrToggleOn();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                    ThingFilterMenuState.CollapseOrToggleOff();
                    Event.current.Use();
                    break;

                case KeyCode.Home:
                    ThingFilterMenuState.JumpToFirst(Event.current.control);
                    Event.current.Use();
                    break;

                case KeyCode.End:
                    ThingFilterMenuState.JumpToLast(Event.current.control);
                    Event.current.Use();
                    break;

                case KeyCode.Backspace:
                    if (ThingFilterMenuState.HasActiveSearch)
                    {
                        ThingFilterMenuState.ProcessBackspace();
                    }
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ThingFilterMenuState.ToggleCurrent();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    if (ThingFilterMenuState.HasActiveSearch)
                    {
                        ThingFilterMenuState.ClearTypeaheadSearch();
                    }
                    else
                    {
                        ThingFilterMenuState.Close();
                        TolkHelper.Speak("Closed thing filter menu");
                    }
                    Event.current.Use();
                    break;

                default:
                    // Consume all remaining keys to prevent leaking to UnifiedKeyboardPatch
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleRangeEditInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    RangeEditMenuState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    RangeEditMenuState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                    RangeEditMenuState.DecreaseValue();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    RangeEditMenuState.IncreaseValue();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Apply changes and return to thing filter menu
                    if (RangeEditMenuState.ApplyAndClose(out var hitPoints, out var quality))
                    {
                        ThingFilterMenuState.ApplyRangeChanges(hitPoints, quality);
                        TolkHelper.Speak("Applied range changes");
                    }
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    RangeEditMenuState.Close();
                    TolkHelper.Speak("Cancelled range editing");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBedAssignmentInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    BedAssignmentState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    BedAssignmentState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    BedAssignmentState.ExecuteSelected();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    BedAssignmentState.GoBack();
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBuildingOwnerAssignmentInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    BuildingOwnerAssignmentState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    BuildingOwnerAssignmentState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    BuildingOwnerAssignmentState.ExecuteSelected();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    BuildingOwnerAssignmentState.GoBack();
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleFlickableComponentInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.Space:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    FlickableComponentState.TogglePower();
                    Event.current.Use();
                    break;

                case KeyCode.D:
                    FlickableComponentState.AnnounceDetailedStatus();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    FlickableComponentState.Close();
                    TolkHelper.Speak("Closed power control menu");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleRefuelableComponentInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    RefuelableComponentState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    RefuelableComponentState.SelectNext();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    RefuelableComponentState.ExecuteSelected();
                    Event.current.Use();
                    break;

                case KeyCode.LeftArrow:
                    RefuelableComponentState.DecreaseTargetFuel();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    RefuelableComponentState.IncreaseTargetFuel();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    RefuelableComponentState.Close();
                    TolkHelper.Speak("Closed fuel settings menu");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBreakdownableComponentInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.R:
                    BreakdownableComponentState.RefreshStatus();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    BreakdownableComponentState.Close();
                    TolkHelper.Speak("Closed breakdown status view");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleDoorControlInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.Space:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    DoorControlState.ToggleHoldOpen();
                    Event.current.Use();
                    break;

                case KeyCode.D:
                    DoorControlState.AnnounceDetailedStatus();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    DoorControlState.Close();
                    TolkHelper.Speak("Closed door controls");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleForbidControlInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.Space:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ForbidControlState.ToggleForbidden();
                    Event.current.Use();
                    break;

                case KeyCode.D:
                    ForbidControlState.AnnounceDetailedStatus();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    ForbidControlState.Close();
                    TolkHelper.Speak("Closed forbid controls");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleFishingZoneMenuInput()
        {
            KeyCode key = Event.current.keyCode;

            // Handle numeric input mode first
            if (FishingZoneMenuState.IsNumericInputMode)
            {
                if (key == KeyCode.Escape)
                {
                    FishingZoneMenuState.CancelNumericInput();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    FishingZoneMenuState.ConfirmNumericInput();
                    Event.current.Use();
                    return;
                }
                if (key == KeyCode.Backspace)
                {
                    FishingZoneMenuState.HandleNumericBackspace();
                    Event.current.Use();
                    return;
                }
                // Handle digit keys using keyCode
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                {
                    char c = (char)('0' + (key - KeyCode.Alpha0));
                    FishingZoneMenuState.HandleNumericDigit(c);
                    Event.current.Use();
                    return;
                }
                if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                {
                    char c = (char)('0' + (key - KeyCode.Keypad0));
                    FishingZoneMenuState.HandleNumericDigit(c);
                    Event.current.Use();
                    return;
                }
                // Ignore other keys in numeric mode
                return;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (FishingZoneMenuState.HasActiveSearch)
                {
                    FishingZoneMenuState.ClearTypeaheadSearch();
                    FishingZoneMenuState.AnnounceWithSearch();
                    Event.current.Use();
                    return;
                }
                FishingZoneMenuState.Close();
                TolkHelper.Speak("Closed fishing zone menu");
                Event.current.Use();
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && FishingZoneMenuState.HasActiveSearch)
            {
                FishingZoneMenuState.ProcessBackspace();
                Event.current.Use();
                return;
            }

            // Handle Alt+I for fish info card
            if (key == KeyCode.I && Event.current.alt)
            {
                FishingZoneMenuState.OpenFishInfoCard();
                Event.current.Use();
                return;
            }

            // Handle typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !Event.current.alt)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                if (!FishingZoneMenuState.ProcessTypeaheadCharacter(c))
                {
                    TolkHelper.Speak($"No matches for '{FishingZoneMenuState.GetLastFailedSearch()}'");
                }
                Event.current.Use();
                return;
            }

            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;

            // Modifier key adjustments
            if (key == KeyCode.UpArrow && shift && !ctrl)
            {
                FishingZoneMenuState.AdjustValue(1, 10);  // Shift+Up = +10
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && shift && !ctrl)
            {
                FishingZoneMenuState.AdjustValue(-1, 10); // Shift+Down = -10
                Event.current.Use();
                return;
            }
            if (key == KeyCode.UpArrow && ctrl && !shift)
            {
                FishingZoneMenuState.AdjustValue(1, 100); // Ctrl+Up = +100
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && ctrl && !shift)
            {
                FishingZoneMenuState.AdjustValue(-1, 100); // Ctrl+Down = -100
                Event.current.Use();
                return;
            }
            if (key == KeyCode.UpArrow && shift && ctrl)
            {
                FishingZoneMenuState.AdjustValue(1, 1000); // Shift+Ctrl+Up = +1000
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow && shift && ctrl)
            {
                FishingZoneMenuState.AdjustValue(-1, 1000); // Shift+Ctrl+Down = -1000
                Event.current.Use();
                return;
            }

            // Min/Max jumps (Shift+Home/End for values)
            if (key == KeyCode.Home && shift && !ctrl)
            {
                FishingZoneMenuState.JumpToMin();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End && shift && !ctrl)
            {
                FishingZoneMenuState.JumpToMax();
                Event.current.Use();
                return;
            }

            // Menu navigation jumps (Home/End and Ctrl+Home/Ctrl+End)
            if (key == KeyCode.Home && !shift)
            {
                FishingZoneMenuState.JumpToFirst(ctrl);
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End && !shift)
            {
                FishingZoneMenuState.JumpToLast(ctrl);
                Event.current.Use();
                return;
            }

            // Handle Arrow Up - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (FishingZoneMenuState.HasActiveSearch && !FishingZoneMenuState.HasNoMatches)
                {
                    int newIndex = FishingZoneMenuState.SelectPreviousMatch();
                    if (newIndex >= 0)
                    {
                        FishingZoneMenuState.SetSelectedIndex(newIndex);
                        FishingZoneMenuState.AnnounceWithSearch();
                    }
                }
                else
                {
                    FishingZoneMenuState.SelectPrevious();
                }
                Event.current.Use();
                return;
            }

            // Handle Arrow Down - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (FishingZoneMenuState.HasActiveSearch && !FishingZoneMenuState.HasNoMatches)
                {
                    int newIndex = FishingZoneMenuState.SelectNextMatch();
                    if (newIndex >= 0)
                    {
                        FishingZoneMenuState.SetSelectedIndex(newIndex);
                        FishingZoneMenuState.AnnounceWithSearch();
                    }
                }
                else
                {
                    FishingZoneMenuState.SelectNext();
                }
                Event.current.Use();
                return;
            }

            switch (key)
            {
                case KeyCode.LeftArrow:
                    FishingZoneMenuState.AdjustValue(-1);
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    FishingZoneMenuState.AdjustValue(1);
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    FishingZoneMenuState.StartNumericInput();
                    Event.current.Use();
                    break;
            }
        }

    }
}
