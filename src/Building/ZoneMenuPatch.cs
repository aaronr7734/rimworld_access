using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add Z key for zone creation menu or zone settings access.
    /// Works at the GUI event level to properly integrate with WindowlessFloatMenuState.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class ZoneMenuPatch
    {
        private static float lastZoneKeyTime = 0f;
        private const float ZoneKeyCooldown = 0.3f;

        // Flag to indicate Z was handled (prevents map search from opening)
        // Stored with the frame number to ensure it persists for the entire frame
        private static int zKeyHandledFrame = -1;
        public static bool ZKeyHandledThisFrame => zKeyHandledFrame == Time.frameCount;

        /// <summary>
        /// Prefix patch to check for Z key press at GUI event level.
        /// Runs before OrderGivingPatch to handle Z key.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)] // Run before OrderGivingPatch
        public static void Prefix()
        {
            // If any accessibility menu is active, don't intercept - let UnifiedKeyboardPatch handle it
            if (KeyboardHelper.IsAnyAccessibilityMenuActive())
                return;

            // Only process keyboard events
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;

            // Only process Z key
            if (key != KeyCode.Z)
                return;

            // Cooldown to prevent accidental double-presses
            if (Time.time - lastZoneKeyTime < ZoneKeyCooldown)
                return;

            lastZoneKeyTime = Time.time;

            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Don't process if windowless orders menu is active
            if (WindowlessFloatMenuState.IsActive)
                return;

            // If already in zone creation mode, don't allow opening menu/settings
            if (ZoneCreationState.IsInCreationMode)
            {
                TolkHelper.Speak("Already creating a zone. Press Enter to confirm or Escape to cancel");
                Event.current.Use();
                zKeyHandledFrame = Time.frameCount;
                return;
            }

            // Get current cursor position
            IntVec3 position = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            // Check if cursor is on a zone
            Zone zone = position.GetZone(map);

            if (zone != null)
            {
                // Open zone settings
                OpenZoneSettings(zone);
            }
            else
            {
                // Show zone creation menu
                ShowZoneCreationMenu();
            }

            // Consume the event and set flag (using frame count to persist for entire frame)
            Event.current.Use();
            zKeyHandledFrame = Time.frameCount;
        }

        /// <summary>
        /// Opens the unified zone settings menu for any zone type.
        /// </summary>
        private static void OpenZoneSettings(Zone zone)
        {
            // Open unified zone settings menu for all zone types
            ZoneSettingsMenuState.Open(zone);
            Log.Message($"Opened zone settings menu for: {zone.label}");
        }

        /// <summary>
        /// Shows a menu for selecting which type of zone to create.
        /// Uses the WindowlessFloatMenuState for accessibility.
        /// </summary>
        private static void ShowZoneCreationMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Stockpile zone option
            options.Add(new FloatMenuOption("Stockpile zone", () =>
            {
                ShowModeSelectionMenu(ZoneType.Stockpile);
            }));

            // Dumping stockpile zone option
            options.Add(new FloatMenuOption("Dumping stockpile zone", () =>
            {
                ShowModeSelectionMenu(ZoneType.DumpingStockpile);
            }));

            // Growing zone option
            options.Add(new FloatMenuOption("Growing zone", () =>
            {
                ShowModeSelectionMenu(ZoneType.GrowingZone);
            }));

            // Home zone option
            options.Add(new FloatMenuOption("Home", () =>
            {
                ShowModeSelectionMenu(ZoneType.HomeZone);
            }));

            // Clear home zone option
            options.Add(new FloatMenuOption("Clear home zone", () =>
            {
                ClearHomeZone();
            }));

            // Allowed area option (at the end)
            options.Add(new FloatMenuOption("Allowed area", () =>
            {
                ShowAllowedAreaNameInputMenu();
            }));

            // Open the windowless menu
            WindowlessFloatMenuState.Open(options, false); // false = doesn't give colonist orders

            Log.Message("Opened zone creation menu");
        }

        /// <summary>
        /// Shows a name input dialog for creating an allowed area.
        /// </summary>
        private static void ShowAllowedAreaNameInputMenu()
        {
            Dialog_NameAllowedArea nameDialog = new Dialog_NameAllowedArea((string name) =>
            {
                // After name is entered, show mode selection
                ZoneCreationState.SetPendingAllowedAreaName(name);
                ShowModeSelectionMenu(ZoneType.AllowedArea);
            });

            Find.WindowStack.Add(nameDialog);
            Log.Message("Opened allowed area name input dialog");
        }

        /// <summary>
        /// Shows a menu for selecting the zone creation mode (Manual, Borders, or Corners).
        /// </summary>
        private static void ShowModeSelectionMenu(ZoneType zoneType)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Manual selection mode
            options.Add(new FloatMenuOption("Manual selection", () =>
            {
                ZoneCreationState.EnterCreationMode(zoneType, ZoneCreationMode.Manual);
            }));

            // Borders mode
            options.Add(new FloatMenuOption("Borders mode", () =>
            {
                ZoneCreationState.EnterCreationMode(zoneType, ZoneCreationMode.Borders);
            }));

            // Corners mode
            options.Add(new FloatMenuOption("Corners mode", () =>
            {
                ZoneCreationState.EnterCreationMode(zoneType, ZoneCreationMode.Corners);
            }));

            // Open the windowless menu
            WindowlessFloatMenuState.Open(options, false);

            Log.Message($"Opened mode selection menu for {zoneType}");
        }

        /// <summary>
        /// Clears all cells from the home zone.
        /// </summary>
        private static void ClearHomeZone()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("No map found", SpeechPriority.High);
                Log.Error("ClearHomeZone: No current map");
                return;
            }

            Area_Home homeArea = map.areaManager.Home;
            if (homeArea == null)
            {
                TolkHelper.Speak("Error: Home area not found", SpeechPriority.High);
                Log.Error("ClearHomeZone: Home area not found in area manager");
                return;
            }

            int previousCount = homeArea.TrueCount;
            homeArea.Clear();

            TolkHelper.Speak($"Home zone cleared. {previousCount} cells removed");
            Log.Message($"Home zone cleared: {previousCount} cells removed");
        }
    }

    /// <summary>
    /// Harmony patch to prevent the map search window from opening when Z is used for zone menu.
    /// Patches WindowStack.Add to intercept Dialog_MapSearch from being added.
    /// </summary>
    [HarmonyPatch(typeof(Verse.WindowStack))]
    [HarmonyPatch("Add")]
    public static class PreventMapSearchWindowPatch
    {
        /// <summary>
        /// Prefix to check if Z key was handled and the window being added is Dialog_MapSearch.
        /// Returns false to prevent the window from being added.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Verse.Window window)
        {
            // If ZoneMenuPatch handled the Z key this frame and this is a map search dialog, block it
            // Also block if any accessibility menu is active (Z should go to typeahead, not game search)
            if ((ZoneMenuPatch.ZKeyHandledThisFrame || KeyboardHelper.IsAnyAccessibilityMenuActive()) && window is RimWorld.Dialog_MapSearch)
            {
                return false; // Prevent window from being added
            }

            return true; // Allow normal window addition
        }
    }
}
