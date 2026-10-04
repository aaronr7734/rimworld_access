using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Tracks the colonist selected by comma/period cycling, per map, and the map switching
    /// Shift+comma/period performs.
    /// </summary>
    public static class PawnSelectionState
    {
        private static int currentSelectedIndex = -1;
        private static Pawn lastSelectedPawn = null;

        /// <summary>The last pawn selected by cycling, or null if none has been.</summary>
        public static Pawn LastSelectedPawn => lastSelectedPawn;

        // Keyed by Map.uniqueID.
        private static Dictionary<int, Pawn> lastSelectedPawnPerMap = new Dictionary<int, Pawn>();

        /// <summary>Selectable colonists on the current map, in colonist-bar order.</summary>
        private static List<Pawn> GetSelectableColonists()
        {
            if (Find.ColonistBar == null)
                return new List<Pawn>();

            var colonists = Find.ColonistBar.GetColonistsInOrder();

            return colonists
                .Where(p => p != null &&
                            p.Spawned &&
                            p.Map == Find.CurrentMap &&
                            p.def.selectable)
                .ToList();
        }

        /// <summary>
        /// Every loaded map, ordered by map ID. Deliberately unfiltered by player presence: a
        /// relevant map can hold no pawns at all (a gravship left with only its anchor), and
        /// vanilla's Game.CurrentMap setter accepts any loaded map.
        /// </summary>
        private static List<Map> GetSwitchableMaps()
        {
            if (Find.Maps == null)
                return new List<Map>();

            return Find.Maps
                .Where(m => m != null)
                .OrderBy(m => m.uniqueID)
                .ToList();
        }

        /// <summary>The map's settlement name, falling back to its ID.</summary>
        private static string GetMapDisplayName(Map map)
        {
            if (map == null)
                return "Unknown";

            if (map.Parent != null && !string.IsNullOrEmpty(map.Parent.LabelCap))
            {
                return map.Parent.LabelCap;
            }

            return $"Map {map.uniqueID}";
        }

        /// <summary>Period: the next colonist in bar order, or null when there are none.</summary>
        public static Pawn SelectNextColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // With a colonist now selected, teach the quick status reads.
            DocsTeacher.Teach("RWA_CheckingColonists");

            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // The remembered pawn may have died or left the map.
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            if (foundIndex == -1)
            {
                currentSelectedIndex = 0;
            }
            else
            {
                currentSelectedIndex = (foundIndex + 1) % colonistList.Count;
                if (currentSelectedIndex == 0 && colonistList.Count > 1)
                    MenuHelper.PlayWrapTone();
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];

            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = lastSelectedPawn;
            }

            return lastSelectedPawn;
        }

        /// <summary>Comma: the previous colonist in bar order, or null when there are none.</summary>
        public static Pawn SelectPreviousColonist()
        {
            var colonistList = GetSelectableColonists();

            if (colonistList.Count == 0)
                return null;

            // With a colonist now selected, teach the quick status reads.
            DocsTeacher.Teach("RWA_CheckingColonists");

            int foundIndex = -1;
            if (lastSelectedPawn != null)
            {
                foundIndex = colonistList.IndexOf(lastSelectedPawn);
            }

            // The remembered pawn may have died or left the map.
            if (foundIndex == -1 && Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                var currentlySelected = Find.Selector.FirstSelectedObject as Pawn;
                if (currentlySelected != null)
                {
                    foundIndex = colonistList.IndexOf(currentlySelected);
                }
            }

            if (foundIndex == -1)
            {
                currentSelectedIndex = colonistList.Count - 1;
            }
            else
            {
                currentSelectedIndex = (foundIndex - 1 + colonistList.Count) % colonistList.Count;
                if (foundIndex == 0 && colonistList.Count > 1)
                    MenuHelper.PlayWrapTone();
            }

            lastSelectedPawn = colonistList[currentSelectedIndex];

            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = lastSelectedPawn;
            }

            return lastSelectedPawn;
        }

        /// <summary>
        /// Switches to the next loaded map and returns the pawn focused there, or null when there
        /// is only one map. <paramref name="presenceInfo"/> reads like "3 colonists, 2 mechs".
        /// </summary>
        public static Pawn SwitchToNextMap(out string mapName, out string presenceInfo)
        {
            return SwitchMap(forward: true, out mapName, out presenceInfo);
        }

        /// <summary>Switches to the previous loaded map; see <see cref="SwitchToNextMap"/>.</summary>
        public static Pawn SwitchToPreviousMap(out string mapName, out string presenceInfo)
        {
            return SwitchMap(forward: false, out mapName, out presenceInfo);
        }

        private static Pawn SwitchMap(bool forward, out string mapName, out string presenceInfo)
        {
            mapName = null;
            presenceInfo = null;

            var maps = GetSwitchableMaps();

            if (maps.Count <= 1)
            {
                return null;
            }

            int currentIndex = maps.FindIndex(m => m == Find.CurrentMap);
            if (currentIndex == -1)
            {
                currentIndex = 0;
            }

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

            Current.Game.CurrentMap = targetMap;

            Pawn pawnToFocus = null;

            if (lastSelectedPawnPerMap.TryGetValue(targetMap.uniqueID, out Pawn rememberedPawn))
            {
                if (rememberedPawn != null && rememberedPawn.Spawned && rememberedPawn.Map == targetMap)
                {
                    pawnToFocus = rememberedPawn;
                }
            }

            // Colonists first, then mechs, then animals.
            if (pawnToFocus == null)
            {
                pawnToFocus = targetMap.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                    ?? targetMap.mapPawns.SpawnedColonyMechs.FirstOrDefault()
                    ?? targetMap.mapPawns.SpawnedColonyAnimals.FirstOrDefault();
            }

            if (pawnToFocus != null)
            {
                lastSelectedPawn = pawnToFocus;
                lastSelectedPawnPerMap[targetMap.uniqueID] = pawnToFocus;
            }

            return pawnToFocus;
        }

        /// <summary>Player presence on a map ("3 colonists, 2 mechs"), or null when there is none.</summary>
        private static string BuildPresenceDescription(Map map)
        {
            int colonists = map.mapPawns.FreeColonistsSpawned.Count();
            int mechs = map.mapPawns.SpawnedColonyMechs.Count();
            int animals = map.mapPawns.SpawnedColonyAnimals.Count();

            var parts = new List<string>();
            if (colonists > 0)
                parts.Add((colonists == 1
                    ? "RimWorldAccess.Pawns.Presence.Colonist"
                    : "RimWorldAccess.Pawns.Presence.Colonists").Translate(colonists));
            if (mechs > 0)
                parts.Add((mechs == 1
                    ? "RimWorldAccess.Pawns.Presence.Mech"
                    : "RimWorldAccess.Pawns.Presence.Mechs").Translate(mechs));
            if (animals > 0)
                parts.Add((animals == 1
                    ? "RimWorldAccess.Pawns.Presence.Animal"
                    : "RimWorldAccess.Pawns.Presence.Animals").Translate(animals));

            if (parts.Count == 0)
                return null;

            return string.Join(", ", parts);
        }

        /// <summary>How many maps the player can switch between.</summary>
        public static int GetMapCount()
        {
            return GetSwitchableMaps().Count;
        }

        /// <summary>Keeps this state in sync with colonist-bar navigation.</summary>
        public static void SyncFromBarNavigation(Pawn pawn)
        {
            if (pawn == null)
                return;

            lastSelectedPawn = pawn;

            if (Find.CurrentMap != null)
            {
                lastSelectedPawnPerMap[Find.CurrentMap.uniqueID] = pawn;
            }

            var colonistList = GetSelectableColonists();
            int idx = colonistList.IndexOf(pawn);
            if (idx >= 0)
                currentSelectedIndex = idx;
        }

        /// <summary>
        /// Turns a pawn-selection action into a cursor redirect while a targeter or a placement
        /// is active. Returns true when it handled the action: the caller must then NOT call
        /// Find.Selector.Select, which would kill the targeter through vanilla's
        /// ConfirmStillValid, and the reinstall designator through CanRemainSelected. Both
        /// predicates are read live: a cached flag could strand the player.
        /// </summary>
        public static bool TryRedirectForTargetingOrPlacement(Pawn pawn)
        {
            if (pawn == null)
                return false;
            if (!ExternalMapTargeting.MapTargetingActive && !ArchitectState.IsInPlacementMode)
                return false;

            // Cursor and camera move only: the Selector is left untouched so the targeter's
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

        /// <summary>Resets the selection state.</summary>
        public static void Reset()
        {
            currentSelectedIndex = -1;
            lastSelectedPawn = null;
            lastSelectedPawnPerMap.Clear();
        }
    }
}
