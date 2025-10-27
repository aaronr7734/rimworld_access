using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle keyboard input for building inspection menus.
    /// Intercepts keyboard events when BuildingInspectState, BillsMenuState, BillConfigState, ThingFilterMenuState, or TempControlMenuState is active.
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
                    ClipboardHelper.CopyToClipboard("Closed building inspection");
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
                    ClipboardHelper.CopyToClipboard("Closed temperature control menu");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBillsMenuInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    BillsMenuState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    BillsMenuState.SelectNext();
                    Event.current.Use();
                    break;

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

                case KeyCode.Escape:
                    BillsMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Closed bills menu");
                    Event.current.Use();
                    break;
            }
        }

        private static void HandleBillConfigInput()
        {
            KeyCode key = Event.current.keyCode;

            switch (key)
            {
                case KeyCode.UpArrow:
                    BillConfigState.SelectPrevious();
                    Event.current.Use();
                    break;

                case KeyCode.DownArrow:
                    BillConfigState.SelectNext();
                    Event.current.Use();
                    break;

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

                case KeyCode.Escape:
                    BillConfigState.Close();
                    ClipboardHelper.CopyToClipboard("Closed bill configuration");

                    // Go back to bills menu
                    if (BuildingInspectState.SelectedBuilding is IBillGiver billGiver)
                    {
                        BillsMenuState.Open(billGiver, BuildingInspectState.SelectedBuilding.Position);
                    }

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
                    ClipboardHelper.CopyToClipboard("Closed thing filter menu");
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
                        ClipboardHelper.CopyToClipboard("Applied range changes");
                    }
                    Event.current.Use();
                    break;

                case KeyCode.Escape:
                    RangeEditMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Cancelled range editing");
                    Event.current.Use();
                    break;
            }
        }
    }
}
