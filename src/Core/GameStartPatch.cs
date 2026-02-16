using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Ensures the view switches from world map to local colony map after game initialization.
    /// During chargen, the world renderer's wantedMode remains WorldRenderMode.Planet from the
    /// starting site selection screen. Nothing in Game.InitNewGame explicitly hides the world view,
    /// so without this patch the player starts the game staring at the world map.
    /// Also fires on game load (which calls FinalizeInit), safely guarded by CurrentMap check.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class GameStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.CurrentMap != null)
                {
                    CameraJumper.TryHideWorld();

                    if (WorldNavigationState.IsActive)
                    {
                        WorldNavigationState.Close();
                    }
                }
            });
        }
    }
}
