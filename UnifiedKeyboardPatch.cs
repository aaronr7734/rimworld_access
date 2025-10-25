using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;

namespace RimWorldAccess
{
    /// <summary>
    /// Unified Harmony patch for UIRoot.UIRootOnGUI to handle all keyboard accessibility features.
    /// Handles: Escape key for pause menu, Enter key for order-giving, and all windowless menu navigation.
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

            // ===== PRIORITY 3: Handle save/load menu if active =====
            if (WindowlessSaveMenuState.IsActive)
            {
                Log.Message($"RimWorld Access: Save menu active, handling key {key}");
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
                Log.Message($"RimWorld Access: Pause menu active, handling key {key}");
                bool handled = false;

                if (key == KeyCode.DownArrow)
                {
                    Log.Message("RimWorld Access: Down arrow in pause menu");
                    WindowlessPauseMenuState.SelectNext();
                    handled = true;
                }
                else if (key == KeyCode.UpArrow)
                {
                    Log.Message("RimWorld Access: Up arrow in pause menu");
                    WindowlessPauseMenuState.SelectPrevious();
                    handled = true;
                }
                else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
                {
                    Log.Message("RimWorld Access: Enter in pause menu");
                    WindowlessPauseMenuState.ExecuteSelected();
                    handled = true;
                }
                else if (key == KeyCode.Escape)
                {
                    Log.Message("RimWorld Access: Escape in pause menu - closing");
                    WindowlessPauseMenuState.Close();
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    Log.Message("RimWorld Access: Event consumed");
                    return;
                }
            }

            // ===== PRIORITY 4.5: Handle options menu if active =====
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
                    ClipboardHelper.CopyToClipboard("Menu closed");
                    handled = true;
                }

                if (handled)
                {
                    Event.current.Use();
                    return;
                }
            }

            // ===== PRIORITY 6: Open pause menu with Escape (if no menu is active and we're in-game) =====
            if (key == KeyCode.Escape)
            {
                Log.Message("RimWorld Access: Escape key pressed");
                // Only open pause menu if:
                // 1. We're in gameplay (not at main menu)
                // 2. No windows are preventing camera motion (means a dialog is open)
                // 3. Not in zone creation mode
                if (Current.ProgramState == ProgramState.Playing &&
                    Find.CurrentMap != null &&
                    (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion) &&
                    !ZoneCreationState.IsInCreationMode)
                {
                    Log.Message("RimWorld Access: Opening pause menu");
                    // Prevent the default escape behavior (opening game's pause menu)
                    Event.current.Use();

                    // Open our windowless pause menu
                    WindowlessPauseMenuState.Open();
                    Log.Message($"RimWorld Access: Pause menu opened, IsActive = {WindowlessPauseMenuState.IsActive}");
                    return;
                }
            }

            // ===== PRIORITY 7: Handle Enter key for order-giving =====
            // Don't process if in zone creation mode
            if (ZoneCreationState.IsInCreationMode)
                return;

            // Only process Enter key for opening menu
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter)
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

            // Check if any pawns are selected
            if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
            {
                ClipboardHelper.CopyToClipboard("No pawn selected");
                Event.current.Use();
                return;
            }

            // Get the cursor position
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            // Validate cursor position
            if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                ClipboardHelper.CopyToClipboard("Invalid position");
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
                ClipboardHelper.CopyToClipboard("No available actions");
            }

            // Consume the event
            Event.current.Use();
        }

}
}
