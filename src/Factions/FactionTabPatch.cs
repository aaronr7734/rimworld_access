using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to intercept the in-game Factions tab and replace it
    /// with the accessible FactionTabState.
    /// Follows the same pattern as ResearchMenuPatch.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Factions), nameof(MainTabWindow_Factions.DoWindowContents))]
    public static class FactionTabPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            // Open our windowless version instead
            FactionTabState.Open();

            // Close the window that was just opened
            Find.WindowStack.TryRemove(typeof(MainTabWindow_Factions), doCloseSound: false);

            // Return false to prevent the original DoWindowContents from executing
            return false;
        }
    }
}
