using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to handle input during zone creation mode.
    /// Uses rectangle selection: Space sets corners, arrows update preview, Enter confirms zone.
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
            IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;

            // Shift+Arrow keys - auto-select to wall (adds cells directly)
            if (shiftHeld && (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                             key == KeyCode.LeftArrow || key == KeyCode.RightArrow))
            {
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
            // Arrow keys (no shift) - update rectangle preview if we have a start corner
            else if (!shiftHeld && ZoneCreationState.HasRectangleStart &&
                     (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                      key == KeyCode.LeftArrow || key == KeyCode.RightArrow))
            {
                // Let MapNavigationState handle the movement first, then we update preview
                // This is handled in a postfix or we rely on the next frame
                // For now, we'll update preview after MapNavigationState moves the cursor
                // The actual movement happens elsewhere, we just need to trigger preview update
            }
            // Space key - set start corner or confirm rectangle
            else if (key == KeyCode.Space)
            {
                // Cooldown to prevent rapid toggling
                if (Time.time - lastSpaceTime < SpaceCooldown)
                    return;

                lastSpaceTime = Time.time;

                if (!ZoneCreationState.HasRectangleStart)
                {
                    // No start corner yet - set it
                    ZoneCreationState.SetRectangleStart(currentPosition);
                }
                else if (ZoneCreationState.IsInPreviewMode)
                {
                    // We have a preview - confirm this rectangle
                    ZoneCreationState.ConfirmRectangle();
                }
                else
                {
                    // Start is set but no end yet - update to create preview at current position
                    ZoneCreationState.UpdatePreview(currentPosition);
                    // Then confirm it
                    ZoneCreationState.ConfirmRectangle();
                }

                handled = true;
            }
            // Enter key - create the zone with all selected cells
            else if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                // If in preview mode, confirm the rectangle first
                if (ZoneCreationState.IsInPreviewMode)
                {
                    ZoneCreationState.ConfirmRectangle();
                }

                // Create the zone
                Map map = Find.CurrentMap;
                ZoneCreationState.CreateZone(map);
                handled = true;
            }
            // Escape key - cancel rectangle or exit zone creation
            else if (key == KeyCode.Escape)
            {
                if (ZoneCreationState.HasRectangleStart)
                {
                    // Cancel current rectangle but stay in creation mode
                    ZoneCreationState.CancelRectangle();
                }
                else
                {
                    // No rectangle in progress - cancel zone creation entirely
                    ZoneCreationState.Cancel();
                }
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
