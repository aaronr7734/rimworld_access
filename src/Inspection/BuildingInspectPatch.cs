using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle keyboard input for building inspection menus.
    /// Intercepts keyboard events when BuildingInspectState, BillsMenuState, BillConfigState, ThingFilterMenuState, TempControlMenuState, or BedAssignmentState is active.
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

            // Handle BuildingInspectState (lowest priority)
            if (BuildingInspectState.IsActive)
            {
                HandleBuildingInspectInput();
                return;
            }
        }

        private static void HandleBuildingInspectInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.LeftArrow:
                    BuildingInspectState.SelectPreviousTab();
                    Event.current.Use();
                    break;

                case KeyCode.RightArrow:
                    BuildingInspectState.SelectNextTab();
                    Event.current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Try to open building settings directly (for coolers, etc.)
                    // If the building doesn't have direct settings, this will open the current tab
                    BuildingInspectState.OpenBuildingSettings();
                    Event.current.Use();
                    break;

                case KeyCode.T:
                    // T key for opening tab-specific menus
                    BuildingInspectState.OpenCurrentTab();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    BuildingInspectState.Close();
                    TolkHelper.Speak("Closed building inspection");
                    Event.current.Use();
                    break;
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

            if ((isLetter || isNumber) && !isExcludedLetter)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                if (!BillsMenuState.ProcessTypeaheadCharacter(c))
                {
                    TolkHelper.Speak($"No matches for '{BillsMenuState.GetLastFailedSearch()}'");
                }
                Event.current.Use();
                return;
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
            KeyCode key = Event.current.keyCode;

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

                // Go back to bills menu
                if (BuildingInspectState.SelectedBuilding is IBillGiver billGiver)
                {
                    BillsMenuState.Open(billGiver, BuildingInspectState.SelectedBuilding.Position);
                }

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

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                if (!BillConfigState.ProcessTypeaheadCharacter(c))
                {
                    TolkHelper.Speak($"No matches for '{BillConfigState.GetLastFailedSearch()}'");
                }
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
                    BillConfigState.ExecuteSelected();
                    Event.current.Use();
                    break;
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

            switch (key)
            {
                case KeyCode.UpArrow:
                    ThingFilterMenuState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
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

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ThingFilterMenuState.ToggleCurrent();
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    ThingFilterMenuState.Close();
                    TolkHelper.Speak("Closed thing filter menu");
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



    }
}
