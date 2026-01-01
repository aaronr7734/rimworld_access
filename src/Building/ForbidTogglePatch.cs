using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to add F key for toggling forbidden status on items at the current cursor position.
    /// Note: Alt+F is handled separately by UnifiedKeyboardPatch for unforbidding all items on the map.
    /// </summary>
    [HarmonyPatch(typeof(CameraDriver))]
    [HarmonyPatch("Update")]
    public static class ForbidTogglePatch
    {
        private static float lastForbidToggleTime = 0f;
        private const float ForbidToggleCooldown = 0.3f; // Prevent accidental double-presses

        /// <summary>
        /// Postfix patch to check for F key press after normal camera updates.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(CameraDriver __instance)
        {
            // Only process during normal gameplay with a valid map
            if (Find.CurrentMap == null || !MapNavigationState.IsInitialized)
                return;

            // Don't process if any dialog or window that prevents camera motion is open
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            // Don't process if windowless orders menu is active
            if (WindowlessFloatMenuState.IsActive)
                return;

            // Don't process if zone rename is active (need to capture text input)
            if (ZoneRenameState.IsActive)
                return;

            // Don't process if any menu with typeahead search is active
            // These menus use letter keys for search, so F would conflict
            if (WindowlessInventoryState.IsActive ||
                WindowlessInspectionState.IsActive ||
                WindowlessResearchMenuState.IsActive ||
                NotificationMenuState.IsActive ||
                QuestMenuState.IsActive ||
                GizmoNavigationState.IsActive ||
                SettlementBrowserState.IsActive ||
                CaravanFormationState.IsActive ||
                StorageSettingsMenuState.IsActive ||
                PlantSelectionMenuState.IsActive ||
                WorkMenuState.IsActive ||
                ModListState.IsActive ||
                ArchitectTreeState.IsActive)
                return;

            // Check for F key press (but not Alt+F, which is handled by UnifiedKeyboardPatch)
            if (Input.GetKeyDown(KeyCode.F) && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                // Cooldown to prevent accidental double-presses
                if (Time.time - lastForbidToggleTime < ForbidToggleCooldown)
                    return;

                lastForbidToggleTime = Time.time;

                // Get current cursor position
                IntVec3 position = MapNavigationState.CurrentCursorPosition;
                Map map = Find.CurrentMap;

                // Get all items at this position
                List<Thing> allThings = position.GetThingList(map);
                List<Thing> forbiddableItems = new List<Thing>();

                // Find all things that can be forbidden
                foreach (Thing thing in allThings)
                {
                    CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                    if (forbiddable != null)
                    {
                        forbiddableItems.Add(thing);
                    }
                }

                if (forbiddableItems.Count == 0)
                {
                    TolkHelper.Speak("Nothing to forbid/unforbid at this location");
                    return;
                }

                // Determine if we should forbid or unforbid
                // If any item is unforbidden, forbid all. If all are forbidden, unforbid all.
                bool shouldForbid = forbiddableItems.Any(t => !t.TryGetComp<CompForbiddable>().Forbidden);

                // Toggle all items
                int toggledCount = 0;
                string firstItemName = null;

                foreach (Thing item in forbiddableItems)
                {
                    if (firstItemName == null)
                        firstItemName = item.LabelShort;

                    item.SetForbidden(shouldForbid, warnOnFail: false);
                    toggledCount++;
                }

                // Announce result
                string announcement;
                if (toggledCount == 1)
                {
                    announcement = shouldForbid 
                        ? $"{firstItemName} forbidden" 
                        : $"{firstItemName} no longer forbidden";
                }
                else
                {
                    announcement = shouldForbid
                        ? $"{toggledCount} items forbidden"
                        : $"{toggledCount} items no longer forbidden";
                }

                TolkHelper.Speak(announcement);
                Log.Message($"Forbid toggle: {announcement}");
            }
        }
    }
}
