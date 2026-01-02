using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add hotkeys for specific tile information categories.
    // Keys 1-7: 1=Items/Pawns, 2=Flooring, 3=Plants, 4=Brightness/Temp, 5=Room Stats, 6=Power, 7=Areas
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("Update")]
    public static class DetailInfoPatch
    {
        private static float lastDetailRequestTime = 0f;
        private const float DetailRequestCooldown = 0.3f; // Prevent spam

        /// <summary>
        /// Postfix patch to check for tile info hotkeys after normal camera updates.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run after other patches
        public static void Postfix(CameraDriver __instance)
        {
            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return;

            // Don't process if in world view (world map has its own number key handling)
            if (WorldNavigationState.IsActive)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Don't process if any menu state is active (to avoid conflicts)
            if (IsAnyMenuActive())
                return;

            // Don't process if Shift is held (Shift+1/2/3 are for time controls)
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;

            // Check for tile info hotkeys (1-7 keys, both alpha and keypad)
            KeyCode? pressedKey = null;
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                pressedKey = KeyCode.Alpha1;
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                pressedKey = KeyCode.Alpha2;
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                pressedKey = KeyCode.Alpha3;
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                pressedKey = KeyCode.Alpha4;
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
                pressedKey = KeyCode.Alpha5;
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
                pressedKey = KeyCode.Alpha6;
            else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7))
                pressedKey = KeyCode.Alpha7;

            if (pressedKey.HasValue)
            {
                // Cooldown to prevent accidental double-presses from causing spam
                if (Time.time - lastDetailRequestTime < DetailRequestCooldown)
                    return;

                lastDetailRequestTime = Time.time;

                // Get appropriate information based on key pressed
                IntVec3 currentPosition = MapNavigationState.CurrentCursorPosition;
                string info = GetInfoForKey(pressedKey.Value, currentPosition, Find.CurrentMap);

                // Copy to clipboard for screen reader
                TolkHelper.Speak(info);

                // Log to console for debugging
                Log.Message($"Tile info ({pressedKey.Value}) requested for {currentPosition}");
            }
        }

        /// <summary>
        /// Checks if any menu state is currently active to avoid key conflicts.
        /// </summary>
        private static bool IsAnyMenuActive()
        {
            return WorkMenuState.IsActive ||
                   ArchitectState.IsActive ||
                   ZoneCreationState.IsInCreationMode ||
                   WindowlessFloatMenuState.IsActive ||
                   WindowlessPauseMenuState.IsActive ||
                   WindowlessSaveMenuState.IsActive ||
                   WindowlessOptionsMenuState.IsActive ||
                   WindowlessConfirmationState.IsActive ||
                   StorageSettingsMenuState.IsActive ||
                   PlantSelectionMenuState.IsActive ||
                   PrisonerTabState.IsActive ||
                   WindowlessInventoryState.IsActive ||
                   WindowlessInspectionState.IsActive ||
                   WindowlessResearchMenuState.IsActive ||
                   NotificationMenuState.IsActive ||
                   QuestMenuState.IsActive ||
                   GizmoNavigationState.IsActive ||
                   SettlementBrowserState.IsActive ||
                   CaravanFormationState.IsActive ||
                   ModListState.IsActive ||
                   HealthTabState.IsActive;
        }

        /// <summary>
        /// Returns the appropriate tile information based on which key was pressed.
        /// </summary>
        private static string GetInfoForKey(KeyCode key, IntVec3 position, Map map)
        {
            switch (key)
            {
                case KeyCode.Alpha1:
                    return TileInfoHelper.GetItemsAndPawnsInfo(position, map);
                case KeyCode.Alpha2:
                    return TileInfoHelper.GetFlooringInfo(position, map);
                case KeyCode.Alpha3:
                    return TileInfoHelper.GetPlantsInfo(position, map);
                case KeyCode.Alpha4:
                    return TileInfoHelper.GetLightInfo(position, map);
                case KeyCode.Alpha5:
                    return TileInfoHelper.GetRoomStatsInfo(position, map);
                case KeyCode.Alpha6:
                    return TileInfoHelper.GetPowerInfo(position, map);
                case KeyCode.Alpha7:
                    return TileInfoHelper.GetAreasInfo(position, map);
                default:
                    return "Unknown key";
            }
        }
    }
}
