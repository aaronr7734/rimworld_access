using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(MainTabWindow_Mechs), nameof(MainTabWindow_Mechs.DoWindowContents))]
    public static class MechsMenuPatch
    {
        private static bool hasIntercepted = false;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!hasIntercepted)
            {
                hasIntercepted = true;

                // If on the world map, switch to colony map first
                if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
                {
                    CameraJumper.TryHideWorld();
                    MapNavigationState.RestoreCursorForCurrentMap();
                }

                MechsMenuState.Open();

                Find.WindowStack.TryRemove(typeof(MainTabWindow_Mechs), doCloseSound: false);

                hasIntercepted = false;

                return false;
            }

            return true;
        }
    }
}
