using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle input during zone creation mode.
    /// Handles Space (add cell), Enter (confirm), and Escape (cancel).
    /// Also modifies arrow key announcements to include "Selected" prefix for selected cells.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class ZoneCreationInputPatch
    {
        private static float lastSpaceTime = 0f;
        private const float SpaceCooldown = 0.2f;

        /// <summary>
        /// Prefix patch to handle zone creation input at GUI event level.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Normal)] // Run after ZoneMenuPatch, before OrderGivingPatch
        public static void Prefix()
        {
            // Only active when in zone creation mode
            if (!ZoneCreationState.IsInCreationMode)
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            // Check we have a valid map
            if (Find.CurrentMap == null)
            {
                ZoneCreationState.Cancel();
                return;
            }

            KeyCode key = Event.current.keyCode;
            bool handled = false;
            bool shiftHeld = Event.current.shift;

            // Shift+Arrow keys - auto-select to wall (only in manual mode)
            if (shiftHeld && ZoneCreationState.CurrentMode == ZoneCreationMode.Manual)
            {
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;
                Rot4 direction = Rot4.Invalid;

                if (key == KeyCode.UpArrow)
                    direction = Rot4.North;
                else if (key == KeyCode.DownArrow)
                    direction = Rot4.South;
                else if (key == KeyCode.LeftArrow)
                    direction = Rot4.West;
                else if (key == KeyCode.RightArrow)
                    direction = Rot4.East;

                if (direction != Rot4.Invalid)
                {
                    ZoneCreationState.AutoSelectToWall(currentPosition, direction, map);
                    handled = true;
                }
            }
            // Space key - toggle selection of current cell
            else if (key == KeyCode.Space)
            {
                // Cooldown to prevent rapid toggling
                if (Time.time - lastSpaceTime < SpaceCooldown)
                    return;

                lastSpaceTime = Time.time;

                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

                if (!ZoneCreationState.IsCellSelected(currentPosition))
                {
                    ZoneCreationState.AddCell(currentPosition);
                }
                else
                {
                    ZoneCreationState.RemoveCell(currentPosition);
                }

                handled = true;
            }
            // Enter key - behavior depends on current mode
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Map map = Find.CurrentMap;

                switch (ZoneCreationState.CurrentMode)
                {
                    case ZoneCreationMode.Manual:
                        // Manual mode: create the zone
                        ZoneCreationState.CreateZone(map);
                        break;

                    case ZoneCreationMode.Borders:
                        // Borders mode: auto-fill interior, then switch to manual
                        ZoneCreationState.BordersModeAutoFill(map);
                        break;

                    case ZoneCreationMode.Corners:
                        // Corners mode: fill rectangle, then switch to manual
                        ZoneCreationState.CornersModeAutoFill(map);
                        break;
                }

                handled = true;
            }
            // Escape key - cancel zone creation
            else if (key == KeyCode.Escape)
            {
                ZoneCreationState.Cancel();
                handled = true;
            }

            if (handled)
            {
                Event.current.Use();
            }
        }
    }


    /// <summary>
    /// Harmony patch to intercept pause key (Space) during zone creation mode.
    /// Prevents Space from pausing the game when in zone creation mode.
    /// </summary>
    [HarmonyPatch(typeof(TimeControls))]
    [HarmonyPatch("DoTimeControlsGUI")]
    public static class ZoneCreationTimeControlsPatch
    {
        /// <summary>
        /// Prefix patch that intercepts the pause key event during zone creation.
        /// Returns false to skip TimeControls processing when Space is pressed in zone creation mode.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            // Only intercept when in zone creation mode
            if (!ZoneCreationState.IsInCreationMode)
                return true; // Continue with normal processing

            // Check if this is a KeyDown event for the pause toggle key
            if (Event.current.type == EventType.KeyDown && 
                KeyBindingDefOf.TogglePause.KeyDownEvent)
            {
                // Consume the event so TimeControls doesn't process it
                Event.current.Use();
                
                // Log for debugging
                Log.Message("Space key intercepted during zone creation mode");
                
                // Don't let TimeControls process this event
                return false;
            }

            // Allow normal processing for other events
            return true;
        }
    }
}
