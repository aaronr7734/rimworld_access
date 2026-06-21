using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of pawn selection cycling for accessibility features.
    /// Tracks the currently selected colonist when cycling with comma and period keys.
    /// Supports multi-map navigation with Shift+comma/period to switch between maps.
    /// </summary>
    public static class PawnSelectionState
    {
        private static int currentSelectedIndex = -1;
        private static Pawn lastSelectedPawn = null;

        /// <summary>
        /// Gets the last pawn selected via comma/period cycling.
        /// Returns null if no pawn has been selected via cycling.
        /// </summary>
        public static Pawn LastSelectedPawn => lastSelectedPawn;

        // Track the last selected pawn per map (using map unique ID as key)
        private static Dictionary<int, Pawn> lastSelectedPawnPerMap = new Dictionary<int, Pawn>();

        /// <summary>
        /// Gets the list of selectable colonists in display order on the current map.
        /// This matches the order shown in the colonist bar.
        /// </summary>
        private static List<Pawn> GetSelectableColonists()
        {
            if (Find.ColonistBar == null)
                return new List<Pawn>();

            // Get colonists in the order they appear in the colonist bar
            var colonists = Find.ColonistBar.GetColonistsInOrder();

            // Filter to only spawned, selectable colonists on the current map
            return colonists
                .Where(p => p != null &&
                            p.Spawned &&
                            p.Map == Find.CurrentMap &&
                            p.def.selectable)
                .ToList();
        }

        /// <summary>
        /// Gets the list of maps the player can navigate to: every map the game is keeping
        /// loaded. Returns maps in a consistent order (by map ID).
        /// </summary>
        /// <remarks>
        /// This intentionally does NOT filter on player presence. The game only retains a map in
        /// Find.Maps while it is relevant to the player, and some relevant maps have no pawns at
        /// all — e.g. a gravship/quest map left holding only a grav anchor after everyone shuttled
        /// off. A presence filter (colonists/mechs/animals/home) excluded those maps, so the user
        /// could no longer switch back to them even though the game still had them loaded. Vanilla's
        /// Game.CurrentMap setter accepts any loaded map; we mirror that.
        /// </remarks>
        private static List<Map> GetSwitchableMaps()
        {
            if (Find.Maps == null)
                return new List<Map>();

            return Find.Maps
                .Where(m => m != null)
                .OrderBy(m => m.uniqueID)
                .ToList();
        }

        /// <summary>
        /// Gets a display name for the map (settlement name or tile description).
        /// </summary>
        private static string GetMapDisplayName(Map map)
        {
            if (map == null)
                return "Unknown";

            // Try to get the settlement/location name from the world object
            if (map.Parent != null && !string.IsNullOrEmpty(map.Parent.LabelCap))
            {
                return map.Parent.LabelCap;
            }

            // Fall back to a generic description
            return $"Map {map.uniqueID}";
        }

        /// <summary>
        /// Selects the next colonist in the list (period key).
        /// Returns the selected pawn, or null if no colonists available.
        /// </summary>
        public static Pawn SelectNextColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // Selecting is taught up front (first map load). Now that a colonist is actually
            // selected, prime the player on the quick status reads — Alt H, N, and M — for
            // hearing how the selected colonist is doing.
            DocsTeacher.Teach("RWA_CheckingColonists");

            // Find the index of the last pawn we selected
            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // If last selected pawn not found (dead, left map, etc.), check current game selection
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            // Calculate next index
            if (foundIndex == -1)
            {
                // No valid previous selection, start at beginning
                currentSelectedIndex = 0;
            }
            else
            {
                // Move to next, wrapping around to start
                currentSelectedIndex = (foundIndex + 1) % colonistList.Count;
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];

            // Remember this pawn as the last selected for this map
            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = lastSelectedPawn;
            }

            return lastSelectedPawn;
        }

        /// <summary>
        /// Selects the previous colonist in the list (comma key).
        /// Returns the selected pawn, or null if no colonists available.
        /// </summary>
        public static Pawn SelectPreviousColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // Selecting is taught up front (first map load). Now that a colonist is actually
            // selected, prime the player on the quick status reads — Alt H, N, and M — for
            // hearing how the selected colonist is doing.
            DocsTeacher.Teach("RWA_CheckingColonists");

            // Find the index of the last pawn we selected
            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // If last selected pawn not found (dead, left map, etc.), check current game selection
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            // Calculate previous index
            if (foundIndex == -1)
            {
                // No valid previous selection, start at end
                currentSelectedIndex = colonistList.Count - 1;
            }
            else
            {
                // Move to previous, wrapping around to end
                currentSelectedIndex = (foundIndex - 1 + colonistList.Count) % colonistList.Count;
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];

            // Remember this pawn as the last selected for this map
            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = lastSelectedPawn;
            }

            return lastSelectedPawn;
        }

        /// <summary>
        /// Switches to the next map that has player presence.
        /// Returns the pawn that was focused on that map (or the first available).
        /// Returns null if there's only one map or no maps with player presence.
        /// </summary>
        /// <param name="mapName">Output: the name of the map we switched to</param>
        /// <param name="presenceInfo">Output: description of player presence on the new map (e.g. "3 colonists, 2 mechs")</param>
        public static Pawn SwitchToNextMap(out string mapName, out string presenceInfo)
        {
            return SwitchMap(forward: true, out mapName, out presenceInfo);
        }

        /// <summary>
        /// Switches to the previous map that has player presence.
        /// Returns the pawn that was focused on that map (or the first available).
        /// Returns null if there's only one map or no maps with player presence.
        /// </summary>
        /// <param name="mapName">Output: the name of the map we switched to</param>
        /// <param name="presenceInfo">Output: description of player presence on the new map (e.g. "3 colonists, 2 mechs")</param>
        public static Pawn SwitchToPreviousMap(out string mapName, out string presenceInfo)
        {
            return SwitchMap(forward: false, out mapName, out presenceInfo);
        }

        /// <summary>
        /// Internal method to switch maps.
        /// </summary>
        private static Pawn SwitchMap(bool forward, out string mapName, out string presenceInfo)
        {
            mapName = null;
            presenceInfo = null;

            var maps = GetSwitchableMaps();

            if (maps.Count <= 1)
            {
                // Only one map (or no maps) - can't switch
                return null;
            }

            // Find current map index
            int currentIndex = maps.FindIndex(m => m == Find.CurrentMap);
            if (currentIndex == -1)
            {
                // Current map not in list (shouldn't happen), start at 0
                currentIndex = 0;
            }

            // Calculate next/previous index
            int newIndex;
            if (forward)
            {
                newIndex = (currentIndex + 1) % maps.Count;
            }
            else
            {
                newIndex = (currentIndex - 1 + maps.Count) % maps.Count;
            }

            Map targetMap = maps[newIndex];
            mapName = GetMapDisplayName(targetMap);
            presenceInfo = BuildPresenceDescription(targetMap);

            // Switch to the new map
            Current.Game.CurrentMap = targetMap;

            // Find the pawn to focus on this map
            Pawn pawnToFocus = null;

            // First, try to use the last selected pawn for this map
            if (lastSelectedPawnPerMap.TryGetValue(targetMap.uniqueID, out Pawn rememberedPawn))
            {
                // Verify the pawn is still valid and on this map
                if (rememberedPawn != null && rememberedPawn.Spawned && rememberedPawn.Map == targetMap)
                {
                    pawnToFocus = rememberedPawn;
                }
            }

            // If no remembered pawn, try colonists first, then mechs, then animals
            if (pawnToFocus == null)
            {
                pawnToFocus = targetMap.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                    ?? targetMap.mapPawns.SpawnedColonyMechs.FirstOrDefault()
                    ?? targetMap.mapPawns.SpawnedColonyAnimals.FirstOrDefault();
            }

            // Update our tracking
            if (pawnToFocus != null)
            {
                lastSelectedPawn = pawnToFocus;
                lastSelectedPawnPerMap[targetMap.uniqueID] = pawnToFocus;
            }

            return pawnToFocus;
        }

        /// <summary>
        /// Builds a human-readable description of player presence on a map.
        /// e.g. "3 colonists, 2 mechs", "1 mech, 4 animals", "No player pawns here"
        /// </summary>
        private static string BuildPresenceDescription(Map map)
        {
            int colonists = map.mapPawns.FreeColonistsSpawned.Count();
            int mechs = map.mapPawns.SpawnedColonyMechs.Count();
            int animals = map.mapPawns.SpawnedColonyAnimals.Count();

            var parts = new List<string>();
            if (colonists > 0)
                parts.Add($"{colonists} {(colonists == 1 ? "colonist" : "colonists")}");
            if (mechs > 0)
                parts.Add($"{mechs} {(mechs == 1 ? "mech" : "mechs")}");
            if (animals > 0)
                parts.Add($"{animals} {(animals == 1 ? "animal" : "animals")}");

            if (parts.Count == 0)
                return null;

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Gets the number of maps the player can switch between.
        /// Useful for checking if map switching is available.
        /// </summary>
        public static int GetMapCount()
        {
            return GetSwitchableMaps().Count;
        }

        /// <summary>
        /// Called by ColonistBarState to keep PawnSelectionState in sync
        /// when the user navigates with Alt+Arrow or Alt+number keys.
        /// </summary>
        public static void SyncFromBarNavigation(Pawn pawn)
        {
            if (pawn == null)
                return;

            lastSelectedPawn = pawn;

            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = pawn;
            }

            // Update index to match
            var colonistList = GetSelectableColonists();
            int idx = colonistList.IndexOf(pawn);
            if (idx >= 0)
                currentSelectedIndex = idx;
        }

        /// <summary>
        /// Redirects a pawn-selection action (comma/period cycling, Alt+1..n bar jump,
        /// Alt+Left/Right bar navigation) when the game's Targeter is currently active.
        /// Returns true if the action was handled as a cursor-redirect (caller must NOT
        /// call Find.Selector.Select afterwards — that would kill the active targeter via
        /// vanilla Targeter.ConfirmStillValid, which calls StopTargeting whenever the
        /// caster is no longer in the Selector). Returns false if no targeting is active
        /// and the caller should perform its normal selection logic.
        ///
        /// Reads Find.Targeter.IsTargeting directly each call: no shadow state, no
        /// caching, so a stale "we think targeting is active" can never strand the user.
        /// The game itself updates IsTargeting real-time (Escape, cast complete, caster
        /// lost, etc.).
        /// </summary>
        public static bool TryRedirectForActiveTargeting(Pawn pawn)
        {
            if (pawn == null)
                return false;
            var targeter = Find.Targeter;
            if (targeter == null || !targeter.IsTargeting)
                return false;

            // Same shape as a bookmark jump: cursor + camera move, terrain audio,
            // standard tile announcement. Selector is left untouched so the targeter's
            // caster-still-selected check keeps passing.
            var map = Find.CurrentMap;
            var pos = pawn.Position;
            MapNavigationState.CurrentCursorPosition = pos;
            if (Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(pos);
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            if (map != null)
            {
                TerrainAudioHelper.PlayCellAudio(pos, map, 0.5f);
                MapNavigationState.LastAnnouncedInfo = "";
                MapArrowKeyHandler.AnnouncePosition(pos, map);
            }
            return true;
        }

        /// <summary>
        /// Resets the selection state.
        /// </summary>
        public static void Reset()
        {
            currentSelectedIndex = -1;
            lastSelectedPawn = null;
            lastSelectedPawnPerMap.Clear();
        }
    }
}
