using HarmonyLib;
using RimWorld;
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
            DocsTeacher.ResetSession();

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
            PlantTargetingState.Reset();

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.CurrentMap != null)
                {
                    CameraJumper.TryHideWorld();

                    if (WorldNavigationState.IsActive)
                    {
                        WorldNavigationState.Close();
                    }

                    // Selecting a colonist is the prerequisite for every order, but at game start the
                    // starting colonists are still descending in drop pods — the map has no spawned
                    // colonists yet. Arm the lesson to fire the moment one actually lands.
                    DocsTeacher.RequestSelectingColonistsWhenPresent();

                    // Controlling time (speed up, pause, read the clock) is one of the first things a
                    // new player needs — they must speed up time just to let the drop pods land. The
                    // vanilla concept is normally surfaced by the desire engine, but our onboarding
                    // lessons crowd out its slot while paused, so teach it explicitly here.
                    DocsTeacher.Teach("TimeControls");

                    // The scanner and forbid/allow lessons both wait until every starting colonist
                    // has landed (all pod cargo down). The scanner surveys the whole map, so it is
                    // most useful once everything is on the ground; forbidding likewise lets the
                    // player free the map after the forbidden items arrive, not before. Firing in the
                    // same poll frame coalesces them into one announcement, matching the time lesson's
                    // promise that a few new lessons appear once everyone has landed.
                    DocsTeacher.RequestMapBasicsWhenAllColonistsPresent();
                }
            });
        }
    }
}
