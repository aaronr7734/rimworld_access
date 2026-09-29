using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard access to RimWorld's WorldRoutePlanner, wrapping the game's own planner so
    /// waypoints stay visible on the map. Waypoints also appear in the World Scanner under
    /// "Route Waypoints", which is <c>WorldScannerState.CreateWaypointsCategory</c>'s surface,
    /// not this one's.
    ///
    /// Row-grammar exception: everything spoken here is a map-flow prompt reacting to a live
    /// world-map cursor, never a focusable row. Every "cursor" in this file is
    /// <c>WorldNavigationState.CurrentSelectedTile</c>, and even
    /// <see cref="RemoveWaypointAtCursor"/> matches by tile rather than by a navigated index, so
    /// there is nothing for <c>ElementDescription</c>/<c>AnnouncementComposer</c> to describe.
    /// </summary>
    public static class RoutePlannerState
    {
        /// <summary>
        /// The game's own waypoint cap (WorldRoutePlanner's private "MaxCount"), read reflectively
        /// so the pre-check cannot drift from vanilla's TryAddWaypoint gate.
        /// </summary>
        private static readonly int MaxWaypoints = ReadMaxWaypoints();

        private static int ReadMaxWaypoints()
        {
            try
            {
                var field = AccessTools.Field(typeof(WorldRoutePlanner), "MaxCount");
                if (field != null && field.FieldType == typeof(int))
                    return (int)field.GetValue(null);
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to read WorldRoutePlanner.MaxCount, falling back to 25: {ex.Message}");
            }
            return 25;
        }

        /// <summary>Whether the game's route planner is active. Safe from the main menu.</summary>
        public static bool IsActive => Find.WorldRoutePlanner?.Active ?? false;

        public static int WaypointCount => Find.WorldRoutePlanner?.waypoints?.Count ?? 0;

        /// <summary>Whose movement speed the travel-time estimates are based on.</summary>
        private static string GetSpeedSourceDescription()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return "";

            if (planner.waypoints.Count > 0)
            {
                Caravan caravanAtStart = Find.WorldObjects?.PlayerControlledCaravanAt(planner.waypoints[0].Tile);
                if (caravanAtStart != null)
                {
                    return (string)"RimWorldAccess.Route.UsingCaravanSpeed".Translate(caravanAtStart.LabelCap);
                }
            }

            return (string)"RimWorldAccess.Route.UsingAverageSpeed".Translate();
        }

        /// <summary>Opens the route planner in standalone mode, not tied to caravan formation.</summary>
        public static void Open()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavMustBeActive".Loc(), SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotAvailable".Loc(), SpeechPriority.High);
                return;
            }

            if (planner.Active)
            {
                Close();
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            planner.Start(currentTile.Layer);

            ResetRouteTracking();

            TolkHelper.Speak("RimWorldAccess.Route.StandaloneInstructions".Loc(), SpeechPriority.Normal);
        }

        /// <summary>Opens the route planner for caravan formation; the first waypoint is locked to the caravan's starting tile.</summary>
        public static void OpenForCaravan(Dialog_FormCaravan formCaravanDialog)
        {
            if (formCaravanDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.NoCaravanDialog".Loc(), SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotAvailable".Loc(), SpeechPriority.High);
                return;
            }

            planner.Start(formCaravanDialog);
            AnnounceCaravanRouteStart();
        }

        /// <summary>The prompt and fresh route tracking for a caravan route choice the planner has just started, whoever started it.</summary>
        public static void AnnounceCaravanRouteStart()
        {
            ResetRouteTracking();

            string addWaypointsPrompt = "RoutePlannerAddOneOrMoreWaypoints".Translate();
            TolkHelper.Speak("RimWorldAccess.Route.ChoosingPrompt".Loc(addWaypointsPrompt), SpeechPriority.Normal);
        }

        /// <summary>Closes the route planner.</summary>
        public static void Close()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return;

            // Capture before Stop(): stopping the planner fires Notify_NoLongerChoosingRoute, which
            // resets CaravanFormationState (clearing the colony-origin reference).
            Map colonyOrigin = CaravanFormationState.RouteChoiceOriginMap;

            planner.Stop();
            ResetRouteTracking();

            if (colonyOrigin != null)
            {
                // The route choice was started from a colony building gizmo (e.g. hitching spot).
                // Cancelling returns the player to that colony map rather than stranding them on
                // the world map.
                if (WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Close();
                }
                CameraJumper.TryHideWorld();
                if (Find.Maps.Contains(colonyOrigin))
                {
                    Current.Game.CurrentMap = colonyOrigin;
                }
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FormationCancelled".Loc(), SpeechPriority.Normal);
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Route.Closed".Loc(), SpeechPriority.Normal);
        }

        /// <summary>Adds a waypoint at the current world navigation tile.</summary>
        public static void AddWaypoint()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Loc(), SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavNotActive".Loc(), SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            if (planner.waypoints.Count >= MaxWaypoints)
            {
                string limitMessage = "MessageCantAddWaypointBecauseLimit".Translate(MaxWaypoints);
                TolkHelper.SpeakData(limitMessage, SpeechPriority.High);
                return;
            }

            int countBefore = planner.waypoints.Count;
            planner.TryAddWaypoint(currentTile, playSound: true);
            int countAfter = planner.waypoints.Count;

            if (countAfter > countBefore)
            {
                if (countAfter >= 2)
                {
                    int ticksToWaypoint = planner.GetTicksToWaypoint(countAfter - 1);
                    string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
                    string speedSource = GetSpeedSourceDescription();
                    TolkHelper.Speak("RimWorldAccess.Route.WaypointAddedWithEta".Loc(countAfter, timeString, speedSource.ToLower()), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Route.WaypointAddedStarting".Loc(countAfter), SpeechPriority.Normal);
                }
            }
        }

        /// <summary>Removes the waypoint at the current cursor tile.</summary>
        public static void RemoveWaypointAtCursor()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Loc(), SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavNotActive".Loc(), SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            RoutePlannerWaypoint waypointAtCursor = null;
            int waypointIndex = -1;
            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile == currentTile)
                {
                    waypointAtCursor = planner.waypoints[i];
                    waypointIndex = i;
                    break;
                }
            }

            if (waypointAtCursor == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.NoWaypointToRemove".Loc(), SpeechPriority.Normal);
                return;
            }

            int countBefore = planner.waypoints.Count;
            planner.TryRemoveWaypoint(waypointAtCursor, playSound: true);
            int countAfter = planner.waypoints.Count;

            if (countAfter < countBefore)
            {
                string message;
                if (countAfter >= 2)
                {
                    int ticksToFinal = planner.GetTicksToWaypoint(countAfter - 1);
                    string timeString = ticksToFinal.ToStringTicksToDays("0.#");
                    string speedSource = GetSpeedSourceDescription();
                    message = "RimWorldAccess.Route.WaypointRemovedWithEta".Translate(waypointIndex + 1, countAfter, timeString, speedSource.ToLower());
                }
                else
                {
                    message = "RimWorldAccess.Route.WaypointRemoved".Translate(waypointIndex + 1, countAfter);
                }

                TolkHelper.SpeakData(message, SpeechPriority.Normal);
            }
        }

        /// <summary>Announces the estimated travel time to the final waypoint.</summary>
        public static void AnnounceETA()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Loc(), SpeechPriority.High);
                return;
            }

            if (planner.waypoints.Count < 2)
            {
                string needMore = "RoutePlannerAddTwoOrMoreWaypoints".Translate();
                TolkHelper.SpeakData(needMore, SpeechPriority.Normal);
                return;
            }

            AnnounceETAToWaypoint(planner.waypoints.Count - 1);
        }

        /// <summary>Announces travel time to a specific waypoint.</summary>
        private static void AnnounceETAToWaypoint(int waypointIndex)
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || waypointIndex < 1 || waypointIndex >= planner.waypoints.Count)
                return;

            int ticksToWaypoint = planner.GetTicksToWaypoint(waypointIndex);
            string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
            string speedSource = GetSpeedSourceDescription();

            TolkHelper.Speak("RimWorldAccess.Route.EtaWithSource".Loc(timeString, speedSource.ToLower()), SpeechPriority.Normal);
        }

        /// <summary>Announces the full route summary.</summary>
        public static void AnnounceRouteSummary()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Loc(), SpeechPriority.High);
                return;
            }

            int count = planner.waypoints.Count;
            if (count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Route.NoWaypointsSet".Loc(), SpeechPriority.Normal);
                return;
            }

            if (count == 1)
            {
                string tileName = WorldInfoHelper.GetTileSummary(planner.waypoints[0].Tile);
                TolkHelper.Speak("RimWorldAccess.Route.StartingPoint".Loc(tileName), SpeechPriority.Normal);
                return;
            }

            int totalTicks = planner.GetTicksToWaypoint(count - 1);
            string timeString = totalTicks.ToStringTicksToDays("0.#");
            string speedSource = GetSpeedSourceDescription();

            string totalEta = "RoutePlannerEstTimeToFinalDest".Translate(timeString);
            TolkHelper.Speak("RimWorldAccess.Route.WaypointSummary".Loc(count, totalEta, speedSource.ToLower()), SpeechPriority.Normal);
        }

        /// <summary>Confirms the route, returning to caravan formation when in caravan mode.</summary>
        public static void ConfirmRoute()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Loc(), SpeechPriority.High);
                return;
            }

            if (!planner.FormingCaravan)
            {
                Close();
                return;
            }

            if (planner.waypoints.Count < 2)
            {
                string needMore = "RoutePlannerAddOneOrMoreWaypoints".Translate();
                TolkHelper.SpeakData(needMore, SpeechPriority.High);
                return;
            }

            // Replicates DoChooseRouteButton, which we cannot click: re-add the dialog, call
            // Notify_ChoseRoute with the FIRST destination only, then stop the planner.

            PlanetTile destination = planner.waypoints[1].Tile;
            string destName = WorldInfoHelper.GetTileSummary(destination, includeRouteInfo: false);

            TolkHelper.Speak("RimWorldAccess.Route.RouteConfirmed".Loc(destName), SpeechPriority.Normal);

            try
            {
                var dialogField = HarmonyLib.AccessTools.Field(typeof(WorldRoutePlanner), "currentFormCaravanDialog");
                var dialog = dialogField?.GetValue(planner) as Dialog_FormCaravan;

                if (dialog != null)
                {
                    // The dialog's map must still exist, or reopening soft-locks: a reformed
                    // caravan removes the map, and vanilla's PostOpen starts the planner for ALL
                    // dialogs, so a stale dialog can survive to be reopened here.
                    var mapField = VanillaAccess.GetField(typeof(Dialog_FormCaravan), "map");
                    var dialogMap = mapField?.GetValue(dialog) as Map;

                    if (dialogMap == null || !Find.Maps.Contains(dialogMap))
                    {
                        TolkHelper.Speak("RimWorldAccess.Route.AlreadySent".Loc(), SpeechPriority.Normal);
                        planner.Stop();
                        ResetRouteTracking();
                        return;
                    }

                    // Set the destination BEFORE re-adding the dialog so its reopen PostOpen sees a
                    // valid destinationTile. CaravanFormationPatch.ShouldChooseRouteFirst relies on
                    // this to distinguish "reopening with a route" from "first open, needs a route".
                    dialog.Notify_ChoseRoute(destination);
                    Find.WindowStack.Add(dialog);
                    planner.Stop();
                }
                else
                {
                    planner.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to confirm route: {ex.Message}");
                planner.Stop();
            }
        }

        #region Shell Focus-Scope Routers

        // Key routers RoutePlannerScope calls. The public action methods above deliberately do
        // NOT carry EnsureLive(): a future non-keyboard caller must not inherit a
        // keyboard-routing yield rule.

        /// <summary>
        /// The routers' shared guard: inactive, or a caravan-formation/split dialog is up and
        /// handles its own input (the game auto-starts the planner for those dialogs). Stays
        /// inside the router methods rather than lifting out to a Claim's when-gate.
        /// </summary>
        private static bool EnsureLive()
        {
            if (!IsActive) return false;
            if (CaravanFormationState.IsActive || SplitCaravanState.IsActive) return false;
            return true;
        }

        /// <summary>Space: add a waypoint at the current tile.</summary>
        public static void HandleAddWaypoint()
        {
            if (!EnsureLive()) return;
            AddWaypoint();
        }

        /// <summary>Shift+Space: remove the waypoint at the current tile.</summary>
        public static void HandleRemoveWaypoint()
        {
            if (!EnsureLive()) return;
            RemoveWaypointAtCursor();
        }

        /// <summary>E: announce the estimated travel time to the final waypoint.</summary>
        public static void HandleAnnounceETA()
        {
            if (!EnsureLive()) return;
            AnnounceETA();
        }

        /// <summary>Enter: confirm the route in caravan-formation mode, else announce the route summary.</summary>
        public static void HandleEnterKey()
        {
            if (!EnsureLive()) return;
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner != null && planner.FormingCaravan)
            {
                ConfirmRoute();
            }
            else
            {
                AnnounceRouteSummary();
            }
        }

        /// <summary>
        /// Escape: close the route planner. No double-close race: WorldRoutePlannerOnGUI opens
        /// with `if (!active) return;` and runs from WorldInterface after UIRootOnGUI, so the
        /// synchronous Stop() here makes vanilla's own cancel branch unreachable this frame. When
        /// this scope yields instead, Close() never runs and vanilla's branch stays the fallback.
        /// </summary>
        public static void HandleCancel()
        {
            if (!EnsureLive()) return;
            Close();
        }

        #endregion

        #region Path Tracing

        /// <summary>Whether the previous tile was on the route, for "Off route" announcements.</summary>
        private static bool wasOnRoute = false;

        /// <summary>
        /// Which path segment the user is on (0-based). Needed because overlapping routes (A→B→A)
        /// reuse the same tiles. Updated when the user reaches a waypoint.
        /// </summary>
        private static int currentPathSegment = 0;

        /// <summary>The current 0-based path segment; segment i runs from waypoint i to waypoint i+1.</summary>
        public static int CurrentPathSegment => currentPathSegment;

        /// <summary>The tileId of the last waypoint the user was at, for detecting arrival at a new one.</summary>
        private static int lastWaypointTileId = -1;

        /// <summary>A tile's position within the planned route.</summary>
        public enum RoutePosition
        {
            NotOnRoute,
            Start,
            Middle,
            Destination
        }

        /// <summary>Information about a tile's position on the route.</summary>
        public struct RouteInfo
        {
            public RoutePosition Position;
            public string NextDirection;  // Cardinal direction to continue (null if at destination)
            public int TicksFromStart;    // Travel time from start to this tile
            public int TicksToDestination; // Travel time from this tile to final destination
            public int WaypointNumber;    // Which waypoint this tile is at (1-based), or 0 if mid-path
        }

        /// <summary>Whether the tile is on the planned route.</summary>
        public static bool IsOnRoute(PlanetTile tile)
        {
            if (!IsActive || !tile.Valid)
                return false;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
                return false;

            int tileId = tile.tileId;
            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile.tileId == tileId)
                    return true;
            }

            var paths = AccessTools.Field(typeof(WorldRoutePlanner), "paths")?.GetValue(planner) as System.Collections.Generic.List<WorldPath>;
            if (paths == null)
                return false;

            foreach (WorldPath path in paths)
            {
                if (path == null || !path.Found)
                    continue;

                // Compare by tileId since PlanetTile equality also checks layerId,
                // which may differ between navigation tiles and path tiles
                foreach (PlanetTile pathTile in path.NodesReversed)
                {
                    if (pathTile.tileId == tileId)
                        return true;
                }
            }

            return false;
        }

        /// <summary>Route information for the tile, or null when it is not on the route.</summary>
        public static RouteInfo? GetRouteInfo(PlanetTile tile)
        {
            if (!IsActive || !tile.Valid)
                return null;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
                return null;

            var paths = AccessTools.Field(typeof(WorldRoutePlanner), "paths")?.GetValue(planner) as System.Collections.Generic.List<WorldPath>;
            if (paths == null)
                return null;

            // Compare by tileId since PlanetTile equality also checks layerId,
            // which may differ between navigation tiles and path tiles
            int tileId = tile.tileId;

            if (planner.waypoints[0].Tile.tileId == tileId)
            {
                string nextDir = GetNavigableRouteDirection(tile, planner, paths);
                return new RouteInfo
                {
                    Position = RoutePosition.Start,
                    NextDirection = nextDir,
                    TicksFromStart = 0,
                    TicksToDestination = planner.GetTicksToWaypoint(planner.waypoints.Count - 1),
                    WaypointNumber = 1
                };
            }

            if (planner.waypoints[planner.waypoints.Count - 1].Tile.tileId == tileId)
            {
                return new RouteInfo
                {
                    Position = RoutePosition.Destination,
                    NextDirection = null,
                    TicksFromStart = planner.GetTicksToWaypoint(planner.waypoints.Count - 1),
                    TicksToDestination = 0,
                    WaypointNumber = planner.waypoints.Count
                };
            }

            for (int i = 1; i < planner.waypoints.Count - 1; i++)
            {
                if (planner.waypoints[i].Tile.tileId == tileId)
                {
                    string nextDir = GetNavigableRouteDirection(tile, planner, paths);
                    int totalTicks = planner.GetTicksToWaypoint(planner.waypoints.Count - 1);
                    int ticksToHere = planner.GetTicksToWaypoint(i);
                    return new RouteInfo
                    {
                        Position = RoutePosition.Middle,
                        NextDirection = nextDir,
                        TicksFromStart = ticksToHere,
                        TicksToDestination = totalTicks - ticksToHere,
                        WaypointNumber = i + 1
                    };
                }
            }

            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                int tileIndex = -1;
                for (int j = 0; j < path.NodesReversed.Count; j++)
                {
                    if (path.NodesReversed[j].tileId == tileId)
                    {
                        tileIndex = j;
                        break;
                    }
                }

                if (tileIndex >= 0)
                {
                    string nextDir = GetNavigableRouteDirection(tile, planner, paths);

                    int ticksToPathEnd = EstimateTicksToTile(tile, path, pathIndex, planner);
                    int totalTicks = planner.GetTicksToWaypoint(planner.waypoints.Count - 1);
                    int ticksToWaypointStart = planner.GetTicksToWaypoint(pathIndex);

                    return new RouteInfo
                    {
                        Position = RoutePosition.Middle,
                        NextDirection = nextDir,
                        TicksFromStart = ticksToPathEnd,
                        TicksToDestination = totalTicks - ticksToPathEnd,
                        WaypointNumber = 0  // Not at a waypoint
                    };
                }
            }

            return null;
        }

        /// <summary>The next tile on the path toward the destination.</summary>
        private static PlanetTile GetNextTileOnPath(PlanetTile currentTile, WorldRoutePlanner planner, System.Collections.Generic.List<WorldPath> paths)
        {
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                int currentIndex = path.NodesReversed.IndexOf(currentTile);
                if (currentIndex >= 0)
                {
                    // NodesReversed runs destination-first, so "next" is the lower index.
                    if (currentIndex > 0)
                    {
                        return path.NodesReversed[currentIndex - 1];
                    }
                    else if (pathIndex < paths.Count - 1)
                    {
                        WorldPath nextPath = paths[pathIndex + 1];
                        if (nextPath != null && nextPath.Found && nextPath.NodesReversed.Count > 1)
                        {
                            // Second-to-last tile of the next path is its first real step.
                            return nextPath.NodesReversed[nextPath.NodesReversed.Count - 2];
                        }
                    }
                    break;
                }
            }
            return PlanetTile.Invalid;
        }

        /// <summary>
        /// The 8-way compass direction to the next tile on the route. Hex-grid neighbours do not
        /// align with the four arrow directions, so the result may be diagonal.
        /// </summary>
        private static string GetNavigableRouteDirection(PlanetTile fromTile, WorldRoutePlanner planner, List<WorldPath> paths)
        {
            if (!fromTile.Valid || Find.WorldGrid == null || paths == null || paths.Count == 0)
                return null;

            int fromTileId = fromTile.tileId;
            PlanetTile nextTileOnPath = PlanetTile.Invalid;

            int startSegment = Math.Max(0, Math.Min(currentPathSegment, paths.Count - 1));

            // Build search order: current segment first, then next, then previous, then rest
            int[] searchOrder = new int[paths.Count];
            int searchIdx = 0;
            searchOrder[searchIdx++] = startSegment;
            if (startSegment + 1 < paths.Count)
                searchOrder[searchIdx++] = startSegment + 1;
            if (startSegment - 1 >= 0)
                searchOrder[searchIdx++] = startSegment - 1;
            for (int i = 0; i < paths.Count; i++)
            {
                bool alreadyAdded = false;
                for (int j = 0; j < searchIdx; j++)
                    if (searchOrder[j] == i) { alreadyAdded = true; break; }
                if (!alreadyAdded)
                    searchOrder[searchIdx++] = i;
            }

            for (int s = 0; s < searchIdx; s++)
            {
                int pathIndex = searchOrder[s];
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                int tileIndex = -1;
                for (int j = 0; j < path.NodesReversed.Count; j++)
                {
                    if (path.NodesReversed[j].tileId == fromTileId)
                    {
                        tileIndex = j;
                        break;
                    }
                }

                if (tileIndex >= 0)
                {
                    List<PlanetTile> neighbors = new List<PlanetTile>();
                    Find.WorldGrid.GetTileNeighbors(fromTile, neighbors);
                    HashSet<int> neighborIds = new HashSet<int>();
                    foreach (var n in neighbors)
                        neighborIds.Add(n.tileId);

                    if (tileIndex > 0)
                    {
                        PlanetTile candidateNext = path.NodesReversed[tileIndex - 1];
                        if (neighborIds.Contains(candidateNext.tileId))
                        {
                            nextTileOnPath = candidateNext;
                        }
                        else
                        {
                            for (int k = tileIndex - 1; k >= 0; k--)
                            {
                                if (neighborIds.Contains(path.NodesReversed[k].tileId))
                                {
                                    nextTileOnPath = path.NodesReversed[k];
                                    break;
                                }
                            }
                        }
                    }

                    if (!nextTileOnPath.Valid && pathIndex < paths.Count - 1)
                    {
                        WorldPath nextPath = paths[pathIndex + 1];
                        if (nextPath != null && nextPath.Found)
                        {
                            for (int k = nextPath.NodesReversed.Count - 1; k >= 0; k--)
                            {
                                int pathTileId = nextPath.NodesReversed[k].tileId;
                                if (pathTileId != fromTileId && neighborIds.Contains(pathTileId))
                                {
                                    nextTileOnPath = nextPath.NodesReversed[k];
                                    break;
                                }
                            }
                        }
                    }
                    break;
                }
            }

            if (!nextTileOnPath.Valid)
                return null;

            return WorldInfoHelper.GetArrowKeyDirection(fromTile, nextTileOnPath);
        }

        /// <summary>Estimated ticks from the route's start to this tile.</summary>
        private static int EstimateTicksToTile(PlanetTile tile, WorldPath path, int pathIndex, WorldRoutePlanner planner)
        {
            int ticksToPathStart = planner.GetTicksToWaypoint(pathIndex);

            int tileIndex = path.NodesReversed.IndexOf(tile);
            int totalNodes = path.NodesReversed.Count;
            if (totalNodes <= 1 || tileIndex < 0)
                return ticksToPathStart;

            // Indexes run destination-first: 0 is this segment's end, count-1 its start.
            float progress = 1f - ((float)tileIndex / (totalNodes - 1));

            int ticksForThisSegment = planner.GetTicksToWaypoint(pathIndex + 1) - ticksToPathStart;
            return ticksToPathStart + (int)(ticksForThisSegment * progress);
        }

        /// <summary>
        /// A short route-status line for the current tile, listing every path segment through it
        /// (routes may double back). Null when off route or the planner is inactive.
        /// </summary>
        public static string GetRouteAnnouncement(PlanetTile tile)
        {
            if (!IsActive || !tile.Valid)
                return null;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
                return null;

            var paths = AccessTools.Field(typeof(WorldRoutePlanner), "paths")?.GetValue(planner) as List<WorldPath>;
            if (paths == null)
                return null;

            int tileId = tile.tileId;

            if (planner.waypoints[planner.waypoints.Count - 1].Tile.tileId == tileId)
            {
                return (string)"RimWorldAccess.Route.AtDestination".Translate();
            }

            // Each entry pairs the target waypoint number with the internal English compass
            // token; the token is localized only at announcement time.
            List<(int waypointNumber, string direction)> segments = new List<(int, string)>();

            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                bool onThisPath = false;
                for (int j = 0; j < path.NodesReversed.Count; j++)
                {
                    if (path.NodesReversed[j].tileId == tileId)
                    {
                        onThisPath = true;
                        break;
                    }
                }

                if (!onThisPath && planner.waypoints[pathIndex].Tile.tileId == tileId)
                {
                    onThisPath = true;
                }

                if (onThisPath)
                {
                    int targetWaypointIndex = pathIndex + 1;
                    if (targetWaypointIndex < planner.waypoints.Count &&
                        planner.waypoints[targetWaypointIndex].Tile.tileId == tileId)
                    {
                        continue;
                    }

                    string direction = GetDirectionForPathSegment(tile, path, pathIndex, paths, planner);
                    if (!string.IsNullOrEmpty(direction))
                    {
                        int targetWaypoint = pathIndex + 2; // paths[0] goes to waypoint 2, etc.
                        segments.Add((targetWaypoint, direction));
                    }
                }
            }

            if (segments.Count == 0)
                return null;

            // Each segment becomes a localized "<dir> to waypoint <n>" phrase; the wrapper key
            // owns the leading verb and trailing punctuation so verb placement stays natural.
            List<string> parts = new List<string>();
            foreach (var segment in segments)
            {
                string localizedDirection = WorldInfoHelper.LocalizeCompass(segment.direction);
                parts.Add((string)"RimWorldAccess.Route.HeadSegment".Translate(localizedDirection, segment.waypointNumber));
            }

            return (string)"RimWorldAccess.Route.HeadDirections".Translate(string.Join(", ", parts));
        }

        /// <summary>The compass direction for one path segment, from the given tile.</summary>
        private static string GetDirectionForPathSegment(PlanetTile fromTile, WorldPath path, int pathIndex, List<WorldPath> paths, WorldRoutePlanner planner)
        {
            if (!fromTile.Valid || Find.WorldGrid == null)
                return null;

            int fromTileId = fromTile.tileId;

            int tileIndex = -1;
            for (int j = 0; j < path.NodesReversed.Count; j++)
            {
                if (path.NodesReversed[j].tileId == fromTileId)
                {
                    tileIndex = j;
                    break;
                }
            }

            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(fromTile, neighbors);
            HashSet<int> neighborIds = new HashSet<int>();
            foreach (var n in neighbors)
                neighborIds.Add(n.tileId);

            PlanetTile nextTile = PlanetTile.Invalid;

            if (tileIndex > 0)
            {
                PlanetTile candidate = path.NodesReversed[tileIndex - 1];
                if (neighborIds.Contains(candidate.tileId))
                    nextTile = candidate;
            }
            else if (tileIndex == 0 || tileIndex == -1)
            {
                if (pathIndex + 1 < paths.Count)
                {
                    WorldPath nextPath = paths[pathIndex + 1];
                    if (nextPath != null && nextPath.Found)
                    {
                        for (int k = nextPath.NodesReversed.Count - 1; k >= 0; k--)
                        {
                            if (neighborIds.Contains(nextPath.NodesReversed[k].tileId) &&
                                nextPath.NodesReversed[k].tileId != fromTileId)
                            {
                                nextTile = nextPath.NodesReversed[k];
                                break;
                            }
                        }
                    }
                }
            }

            if (!nextTile.Valid)
                return null;

            return WorldInfoHelper.GetArrowKeyDirection(fromTile, nextTile);
        }

        /// <summary>
        /// Tracks which path segment the user is on, for overlapping routes. Call after each tile
        /// change while the planner is active.
        /// </summary>
        public static void CheckOffRoute(PlanetTile currentTile)
        {
            if (!IsActive)
            {
                wasOnRoute = false;
                currentPathSegment = 0;
                lastWaypointTileId = -1;
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
            {
                wasOnRoute = false;
                return;
            }

            int currentTileId = currentTile.tileId;
            bool nowOnRoute = IsOnRoute(currentTile);

            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile.tileId == currentTileId)
                {
                    if (currentTileId != lastWaypointTileId)
                    {
                        lastWaypointTileId = currentTileId;

                        // At waypoint i the segment is i, except at the final waypoint where the
                        // last segment stands.
                        if (i < planner.waypoints.Count - 1)
                        {
                            currentPathSegment = i;
                        }
                    }
                    break;
                }
            }

            bool atWaypoint = false;
            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile.tileId == currentTileId)
                {
                    atWaypoint = true;
                    break;
                }
            }
            if (!atWaypoint)
            {
                lastWaypointTileId = -1;
            }

            // No "Off route" announcement: hex navigation often requires stepping off the
            // computed path, since arrow keys move in only four directions.

            wasOnRoute = nowOnRoute;
        }

        /// <summary>
        /// (nextWaypointNumber, ticksFromStart) for the current segment, or null when off route.
        /// ticksFromStart is cumulative from waypoint 1, matching the game's own reckoning.
        /// </summary>
        public static (int waypointNumber, int ticksFromStart)? GetCurrentSegmentTiming(PlanetTile tile)
        {
            if (!IsActive || !tile.Valid)
                return null;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
                return null;

            var paths = AccessTools.Field(typeof(WorldRoutePlanner), "paths")?.GetValue(planner) as List<WorldPath>;
            if (paths == null || currentPathSegment >= paths.Count)
                return null;

            int targetWaypointIndex = currentPathSegment + 1;
            if (targetWaypointIndex >= planner.waypoints.Count)
                return null;

            int ticksToSegmentStart = planner.GetTicksToWaypoint(currentPathSegment);

            int ticksToTargetWaypoint = planner.GetTicksToWaypoint(targetWaypointIndex);
            int segmentTicks = ticksToTargetWaypoint - ticksToSegmentStart;

            WorldPath currentPath = paths[currentPathSegment];
            if (currentPath == null || !currentPath.Found)
                return null;

            int tileId = tile.tileId;
            int tileIndex = -1;
            for (int j = 0; j < currentPath.NodesReversed.Count; j++)
            {
                if (currentPath.NodesReversed[j].tileId == tileId)
                {
                    tileIndex = j;
                    break;
                }
            }

            int ticksFromStart;
            if (tileIndex >= 0)
            {
                int totalNodes = currentPath.NodesReversed.Count;
                // NodesReversed runs destination-first, so progress = 1 - tileIndex/(total-1).
                // At destination (index 0): progress = 1.0
                // At start (index totalNodes-1): progress = 0.0
                float progressFraction = (totalNodes > 1) ? 1f - ((float)tileIndex / (totalNodes - 1)) : 1f;
                ticksFromStart = ticksToSegmentStart + (int)(segmentTicks * progressFraction);
            }
            else
            {
                ticksFromStart = ticksToSegmentStart;
            }

            return (targetWaypointIndex + 1, ticksFromStart);
        }

        /// <summary>Resets route tracking; called when the planner opens or closes.</summary>
        public static void ResetRouteTracking()
        {
            wasOnRoute = false;
            currentPathSegment = 0;
            lastWaypointTileId = -1;
        }

        #endregion
    }
}
