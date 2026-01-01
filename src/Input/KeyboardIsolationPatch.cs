using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that blocks ALL keyboard input from reaching the game when any
    /// accessibility menu is active. This patch runs at Priority.Last, after all other
    /// keyboard handlers, and consumes any keyboard events that weren't already handled.
    ///
    /// This prevents issues like:
    /// - Arrow keys panning the camera while navigating menus
    /// - Letter keys triggering game shortcuts during typeahead search
    /// - Any other keyboard shortcuts interfering with accessibility navigation
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class KeyboardIsolationPatch
    {
        /// <summary>
        /// Runs after all other keyboard patches (Priority.Last).
        /// Consumes any remaining keyboard events when an accessibility menu is active.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix()
        {
            // Only process if an accessibility menu is active
            if (!KeyboardHelper.IsAnyAccessibilityMenuActive())
                return;

            // Consume ALL keyboard events to prevent them from reaching the game
            if (Event.current.type == EventType.KeyDown ||
                Event.current.type == EventType.KeyUp)
            {
                Event.current.Use();
            }
        }
    }
}
