using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for CameraJumper.TryJumpAndSelect to set WorldNavigationState.PendingStartTile
    /// before jumping to world targets. This ensures that when WorldNavigationState.Open() is called
    /// in the next frame (when WorldNavigationPatch detects the mode change), it uses the correct tile
    /// instead of defaulting to the colony.
    ///
    /// This fixes the issue where pressing "Jump to Location" on a caravan letter would place the
    /// cursor at the colony instead of the caravan.
    /// </summary>
    [HarmonyPatch(typeof(CameraJumper))]
    [HarmonyPatch("TryJumpAndSelect")]
    [HarmonyPatch(new[] { typeof(GlobalTargetInfo), typeof(CameraJumper.MovementMode) })]
    public static class CameraJumperPatch
    {
        /// <summary>
        /// Prefix patch that sets PendingStartTile for world targets before the jump occurs.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(GlobalTargetInfo target)
        {
            if (!target.IsValid)
                return;

            // For world targets, set pending tile so WorldNavigationState.Open() uses it
            if (target.HasWorldObject)
            {
                PlanetTile tile = target.WorldObject.Tile;
                if (tile.Valid)
                {
                    WorldNavigationState.PendingStartTile = tile;
                }
            }
            else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
            {
                // World tile target (not a thing or cell)
                WorldNavigationState.PendingStartTile = target.Tile;
            }
        }

        /// <summary>
        /// Postfix patch that updates CurrentSelectedTile in case world view was already open.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(GlobalTargetInfo target)
        {
            if (!target.IsValid)
                return;

            // Also set current tile in case world view was already open (Open() won't be called)
            if (target.HasWorldObject)
            {
                PlanetTile tile = target.WorldObject.Tile;
                if (tile.Valid)
                {
                    WorldNavigationState.CurrentSelectedTile = tile;
                }
            }
            else if (target.Tile.Valid && !target.HasThing && !target.Cell.IsValid)
            {
                WorldNavigationState.CurrentSelectedTile = target.Tile;
            }
            else if (MapNavigationState.IsInitialized)
            {
                // Map target - update map cursor
                if (target.HasThing)
                    MapNavigationState.CurrentCursorPosition = target.Thing.Position;
                else if (target.Cell.IsValid)
                    MapNavigationState.CurrentCursorPosition = target.Cell;
            }
        }
    }
}
