using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch to intercept the in-game Ideology tab and replace it
    /// with the accessible IdeologyTabState.
    /// Follows the same windowless pattern as FactionTabPatch.
    /// </summary>
    /// <summary>
    /// Per-frame sustainer maintenance for ritual ambience sound preview.
    /// Runs every frame so the sustainer stays alive while playing.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class IdeologyRitualSoundMaintainPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IdeologyTreeNavigation.MaintainRitualSound();
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Ideos), nameof(MainTabWindow_Ideos.DoWindowContents))]
    public static class IdeologyTabPatch
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
            IdeologyTabState.Open();

            // Close the window that was just opened
            Find.WindowStack.TryRemove(typeof(MainTabWindow_Ideos), doCloseSound: false);

            // Return false to prevent the original DoWindowContents from executing
            return false;
        }
    }
}
