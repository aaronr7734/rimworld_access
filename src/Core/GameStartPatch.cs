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
            MultiSelectState.Reset();

            // Loading a save from in-game never passes through the main menu or a
            // CurrentMap == null frame, so no other reset path fires. The old session's
            // Things are never despawned (ClearAllMapsAndWorld just drops them) and still
            // report Spawned == true, and the reloaded map keeps its saved uniqueID — so
            // stale caches pass every liveness and map-identity check, and the scanner's
            // thing-state hash (a bare spawn/despawn counter) can collide between two
            // saves of the same colony. Reset all ambient map-session state here;
            // FinalizeInit fires on both new games and saves loaded from anywhere.
            MapNavigationState.Reset(); // also invalidates ScannerState + ScannerHelper caches
            PawnSelectionState.Reset();
            WorldScannerState.Reset();

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
