using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Provides keyboard accessibility for RimWorld's WorldRoutePlanner.
    /// This wraps the game's actual route planner, so waypoints are visible on the map.
    ///
    /// Controls:
    ///   R (on world map) - Toggle route planner
    ///   Space - Add waypoint at current tile
    ///   Delete/Backspace - Remove last waypoint
    ///   E - Announce ETA to final waypoint
    ///   Escape - Exit route planner
    ///
    /// Waypoints appear in the World Scanner under "Route Waypoints" category.
    /// </summary>
    public static class RoutePlannerState
    {
        /// <summary>
        /// Gets whether the route planner is currently active.
        /// Wraps the game's WorldRoutePlanner.Active state.
        /// Safe to call from main menu (returns false due to null-conditional).
        /// </summary>
        public static bool IsActive => Find.WorldRoutePlanner?.Active ?? false;

        /// <summary>
        /// Gets the current waypoint count.
        /// </summary>
        public static int WaypointCount => Find.WorldRoutePlanner?.waypoints?.Count ?? 0;

        /// <summary>
        /// Gets a description of whose movement speed is being used for travel time calculations.
        /// </summary>
        private static string GetSpeedSourceDescription()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return "";

            // Check if there's a caravan at the first waypoint
            if (planner.waypoints.Count > 0)
            {
                Caravan caravanAtStart = Find.WorldObjects?.PlayerControlledCaravanAt(planner.waypoints[0].Tile);
                if (caravanAtStart != null)
                {
                    return $"Using {caravanAtStart.LabelCap}'s speed";
                }
            }

            // No caravan - using average speed
            return "Using average caravan speed";
        }

        /// <summary>
        /// Opens the route planner in standalone mode (not tied to caravan formation).
        /// </summary>
        public static void Open()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavMustBeActive".Translate(), SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotAvailable".Translate(), SpeechPriority.High);
                return;
            }

            if (planner.Active)
            {
                // Already active, close it
                Close();
                return;
            }

            // Get current tile's layer for the route planner
            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            // Start the route planner
            planner.Start(currentTile.Layer);

            // Reset path tracking state
            ResetRouteTracking();

            // Use game's localized string for instructions
            // "RoutePlannerPressRMBToAddAndRemoveWaypoints" is visual-specific, so we provide our own
            TolkHelper.Speak("RimWorldAccess.Route.StandaloneInstructions".Translate(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens the route planner for caravan formation.
        /// The first waypoint is locked to the caravan's starting location.
        /// </summary>
        public static void OpenForCaravan(Dialog_FormCaravan formCaravanDialog)
        {
            if (formCaravanDialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.NoCaravanDialog".Translate(), SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotAvailable".Translate(), SpeechPriority.High);
                return;
            }

            // Start route planner in caravan formation mode
            // This locks the first waypoint to the caravan's starting tile
            planner.Start(formCaravanDialog);

            // Reset path tracking state
            ResetRouteTracking();

            // Use localized string where appropriate
            string addWaypointsPrompt = "RoutePlannerAddOneOrMoreWaypoints".Translate();
            TolkHelper.Speak("RimWorldAccess.Route.ChoosingPrompt".Translate(addWaypointsPrompt), SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes the route planner.
        /// </summary>
        public static void Close()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return;

            planner.Stop();
            ResetRouteTracking();
            TolkHelper.Speak("RimWorldAccess.Route.Closed".Translate(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Adds a waypoint at the current world navigation tile.
        /// </summary>
        public static void AddWaypoint()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Translate(), SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavNotActive".Translate(), SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            // Check waypoint limit (game uses 25)
            if (planner.waypoints.Count >= 25)
            {
                // Use game's localized message
                string limitMessage = "MessageCantAddWaypointBecauseLimit".Translate(25);
                TolkHelper.Speak(limitMessage, SpeechPriority.High);
                return;
            }

            // TryAddWaypoint handles validation (impassable, unreachable, etc.) and shows messages
            int countBefore = planner.waypoints.Count;
            planner.TryAddWaypoint(currentTile, playSound: true);
            int countAfter = planner.waypoints.Count;

            if (countAfter > countBefore)
            {
                // Successfully added - keep announcement concise
                if (countAfter >= 2)
                {
                    // Include travel time and speed source in the same announcement
                    int ticksToWaypoint = planner.GetTicksToWaypoint(countAfter - 1);
                    string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
                    string speedSource = GetSpeedSourceDescription();
                    TolkHelper.Speak("RimWorldAccess.Route.WaypointAddedWithEta".Translate(countAfter, timeString, speedSource.ToLower()), SpeechPriority.Normal);
                }
                else
                {
                    // First waypoint (starting point)
                    TolkHelper.Speak("RimWorldAccess.Route.WaypointAddedStarting".Translate(countAfter), SpeechPriority.Normal);
                }
            }
            // If count didn't change, TryAddWaypoint already showed an error message
        }

        /// <summary>
        /// Removes the waypoint at the current cursor position.
        /// </summary>
        public static void RemoveWaypointAtCursor()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Translate(), SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Route.WorldNavNotActive".Translate(), SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High)) return;

            // Find waypoint at current tile
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
                TolkHelper.Speak("RimWorldAccess.Route.NoWaypointToRemove".Translate(), SpeechPriority.Normal);
                return;
            }

            // TryRemoveWaypoint handles validation (can't remove first in caravan mode) and shows messages
            int countBefore = planner.waypoints.Count;
            planner.TryRemoveWaypoint(waypointAtCursor, playSound: true);
            int countAfter = planner.waypoints.Count;

            if (countAfter < countBefore)
            {
                string message;
                // If we still have 2+ waypoints, also announce travel time to final destination
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

                TolkHelper.Speak(message, SpeechPriority.Normal);
            }
            // If count didn't change, TryRemoveWaypoint already showed an error message (e.g., can't remove first waypoint)
        }

        /// <summary>
        /// Announces the estimated travel time to the final waypoint.
        /// </summary>
        public static void AnnounceETA()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Translate(), SpeechPriority.High);
                return;
            }

            if (planner.waypoints.Count < 2)
            {
                // Use game's localized string
                string needMore = "RoutePlannerAddTwoOrMoreWaypoints".Translate();
                TolkHelper.Speak(needMore, SpeechPriority.Normal);
                return;
            }

            AnnounceETAToWaypoint(planner.waypoints.Count - 1);
        }

        /// <summary>
        /// Announces travel time to a specific waypoint.
        /// </summary>
        private static void AnnounceETAToWaypoint(int waypointIndex)
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || waypointIndex < 1 || waypointIndex >= planner.waypoints.Count)
                return;

            int ticksToWaypoint = planner.GetTicksToWaypoint(waypointIndex);
            string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
            string speedSource = GetSpeedSourceDescription();

            // Include speed source so user knows basis for estimate
            TolkHelper.Speak("RimWorldAccess.Route.EtaWithSource".Translate(timeString, speedSource.ToLower()), SpeechPriority.Normal);
        }

        /// <summary>
        /// Announces the full route summary.
        /// </summary>
        public static void AnnounceRouteSummary()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Translate(), SpeechPriority.High);
                return;
            }

            int count = planner.waypoints.Count;
            if (count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Route.NoWaypointsSet".Translate(), SpeechPriority.Normal);
                return;
            }

            if (count == 1)
            {
                string tileName = WorldInfoHelper.GetTileSummary(planner.waypoints[0].Tile);
                TolkHelper.Speak("RimWorldAccess.Route.StartingPoint".Translate(tileName), SpeechPriority.Normal);
                return;
            }

            // Multiple waypoints - announce total time
            int totalTicks = planner.GetTicksToWaypoint(count - 1);
            string timeString = totalTicks.ToStringTicksToDays("0.#");
            string speedSource = GetSpeedSourceDescription();

            // Use game's localized string
            string totalEta = "RoutePlannerEstTimeToFinalDest".Translate(timeString);
            TolkHelper.Speak("RimWorldAccess.Route.WaypointSummary".Translate(count, totalEta, speedSource.ToLower()), SpeechPriority.Normal);
        }

        /// <summary>
        /// Confirms the route and returns to caravan formation (if in caravan mode).
        /// </summary>
        public static void ConfirmRoute()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Route.PlannerNotActive".Translate(), SpeechPriority.High);
                return;
            }

            if (!planner.FormingCaravan)
            {
                // Not in caravan mode - just close
                Close();
                return;
            }

            if (planner.waypoints.Count < 2)
            {
                string needMore = "RoutePlannerAddOneOrMoreWaypoints".Translate();
                TolkHelper.Speak(needMore, SpeechPriority.High);
                return;
            }

            // The game's DoChooseRouteButton does:
            // 1. Adds the dialog back to window stack
            // 2. Calls Notify_ChoseRoute(waypoints[1].Tile) - only the FIRST destination
            // 3. Stops the route planner
            // We need to replicate this since we can't click the button

            // Get the destination (first waypoint after starting point)
            PlanetTile destination = planner.waypoints[1].Tile;
            // Don't include route info - we already say "Destination:" and don't need "Destination." appended
            string destName = WorldInfoHelper.GetTileSummary(destination, includeRouteInfo: false);

            // The route planner will handle returning to the dialog when Stop() is called
            // if currentFormCaravanDialog is set
            TolkHelper.Speak("RimWorldAccess.Route.RouteConfirmed".Translate(destName), SpeechPriority.Normal);

            // Trigger the accept action - this will close route planner and return to dialog
            // We use reflection to access currentFormCaravanDialog and call Notify_ChoseRoute
            try
            {
                var dialogField = HarmonyLib.AccessTools.Field(typeof(WorldRoutePlanner), "currentFormCaravanDialog");
                var dialog = dialogField?.GetValue(planner) as Dialog_FormCaravan;

                if (dialog != null)
                {
                    // SAFETY CHECK: Verify the dialog's map still exists before trying to reopen
                    // This prevents soft locks when:
                    // 1. User reformed a caravan (Shift+C) - map was removed after TryReformCaravan
                    // 2. Route planner wasn't properly stopped (game bug - PostOpen starts it for ALL dialogs)
                    // 3. User presses Enter on world map, we try to reopen a stale dialog
                    var mapField = HarmonyLib.AccessTools.Field(typeof(Dialog_FormCaravan), "map");
                    var dialogMap = mapField?.GetValue(dialog) as Map;

                    if (dialogMap == null || !Find.Maps.Contains(dialogMap))
                    {
                        // Map no longer exists - caravan was already reformed/sent
                        // Just stop the route planner, don't try to reopen the stale dialog
                        TolkHelper.Speak("RimWorldAccess.Route.AlreadySent".Translate(), SpeechPriority.Normal);
                        planner.Stop();
                        ResetRouteTracking();
                        return;
                    }

                    Find.WindowStack.Add(dialog);
                    dialog.Notify_ChoseRoute(destination);
                    planner.Stop();
                }
                else
                {
                    // Fallback - just stop
                    planner.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RimWorld Access: Failed to confirm route: {ex.Message}");
                planner.Stop();
            }
        }

        /// <summary>
        /// Handles keyboard input for the route planner.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive)
                return false;

            // Don't handle input when caravan formation/split dialog is active
            // The game starts the route planner automatically for those dialogs,
            // but they handle their own input including Escape
            if (CaravanFormationState.IsActive || SplitCaravanState.IsActive)
                return false;

            // Space - Add waypoint (no modifiers)
            if (key == KeyCode.Space && !shift && !ctrl && !alt)
            {
                AddWaypoint();
                return true;
            }

            // Shift+Space - Remove waypoint at cursor (consistent with colony map structure removal)
            if (key == KeyCode.Space && shift && !ctrl && !alt)
            {
                RemoveWaypointAtCursor();
                return true;
            }

            // E - Announce ETA
            if (key == KeyCode.E && !shift && !ctrl && !alt)
            {
                AnnounceETA();
                return true;
            }

            // Enter - Confirm route (in caravan mode) or announce summary (standalone)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                WorldRoutePlanner planner = Find.WorldRoutePlanner;
                if (planner != null && planner.FormingCaravan)
                {
                    ConfirmRoute();
                }
                else
                {
                    AnnounceRouteSummary();
                }
                return true;
            }

            // Escape - Close route planner
            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                Close();
                return true;
            }

            // Let other keys pass through (arrow keys for navigation, scanner keys, etc.)
            return false;
        }

        #region Path Tracing

        /// <summary>
        /// Tracks whether the previous tile was on the route (for "Off route" announcements).
        /// </summary>
        private static bool wasOnRoute = false;

        /// <summary>
        /// Tracks which path segment the user is currently on (0-based index into paths list).
        /// This is needed when paths overlap (e.g., going A→B→A uses the same tiles twice).
        /// Updated when the user reaches a waypoint.
        /// </summary>
        private static int currentPathSegment = 0;

        /// <summary>
        /// Gets the current path segment index (0-based).
        /// Path segment i goes from waypoint i to waypoint i+1.
        /// </summary>
        public static int CurrentPathSegment => currentPathSegment;

        /// <summary>
        /// The tileId of the last waypoint the user was at.
        /// Used to detect when user reaches a new waypoint.
        /// </summary>
        private static int lastWaypointTileId = -1;

        /// <summary>
        /// Represents the position of a tile within the planned route.
        /// </summary>
        public enum RoutePosition
        {
            NotOnRoute,
            Start,
            Middle,
            Destination
        }

        /// <summary>
        /// Information about a tile's position on the route.
        /// </summary>
        public struct RouteInfo
        {
            public RoutePosition Position;
            public string NextDirection;  // Cardinal direction to continue (null if at destination)
            public int TicksFromStart;    // Travel time from start to this tile
            public int TicksToDestination; // Travel time from this tile to final destination
            public int WaypointNumber;    // Which waypoint this tile is at (1-based), or 0 if mid-path
        }

        /// <summary>
        /// Checks if the specified tile is on the planned route.
        /// </summary>
        public static bool IsOnRoute(PlanetTile tile)
        {
            if (!IsActive || !tile.Valid)
                return false;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || planner.waypoints.Count < 2)
                return false;

            // Check if it's a waypoint (compare tileId to handle layer differences)
            int tileId = tile.tileId;
            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile.tileId == tileId)
                    return true;
            }

            // Check path segments
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

        /// <summary>
        /// Gets route information for the specified tile.
        /// Returns null if tile is not on the route.
        /// </summary>
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

            // Check if tile is the start waypoint
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

            // Check if tile is the final waypoint
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

            // Check if tile is an intermediate waypoint
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

            // Check if tile is on a path segment (not a waypoint)
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                // Find tile index by comparing tileId
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
                    // Found on this path segment
                    string nextDir = GetNavigableRouteDirection(tile, planner, paths);

                    // Calculate time estimates
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

        /// <summary>
        /// Gets the next tile on the path toward the destination.
        /// </summary>
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
                    // NodesReversed is reversed: index 0 = destination, higher index = toward start
                    // So "next toward destination" means lower index
                    if (currentIndex > 0)
                    {
                        return path.NodesReversed[currentIndex - 1];
                    }
                    else if (pathIndex < paths.Count - 1)
                    {
                        // At the end of this path segment, get first tile of next segment
                        WorldPath nextPath = paths[pathIndex + 1];
                        if (nextPath != null && nextPath.Found && nextPath.NodesReversed.Count > 1)
                        {
                            // Return second-to-last tile of next path (the first real step)
                            return nextPath.NodesReversed[nextPath.NodesReversed.Count - 2];
                        }
                    }
                    break;
                }
            }
            return PlanetTile.Invalid;
        }

        /// <summary>
        /// Gets the compass direction to continue on the route (supports 8 directions).
        /// Returns the actual direction to the next tile on the path, which may be a diagonal
        /// like "northeast" or "southwest". On a hex grid, caravans can move to any of 6 neighbors,
        /// so the path direction may not align with the 4 arrow key directions.
        /// </summary>
        private static string GetNavigableRouteDirection(PlanetTile fromTile, WorldRoutePlanner planner, List<WorldPath> paths)
        {
            if (!fromTile.Valid || Find.WorldGrid == null || paths == null || paths.Count == 0)
                return null;

            int fromTileId = fromTile.tileId;
            PlanetTile nextTileOnPath = PlanetTile.Invalid;

            // Clamp currentPathSegment to valid range
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

            // Search paths in order to find next tile
            for (int s = 0; s < searchIdx; s++)
            {
                int pathIndex = searchOrder[s];
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                // Find tile in this path
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
                    // Get neighbors for validation
                    List<PlanetTile> neighbors = new List<PlanetTile>();
                    Find.WorldGrid.GetTileNeighbors(fromTile, neighbors);
                    HashSet<int> neighborIds = new HashSet<int>();
                    foreach (var n in neighbors)
                        neighborIds.Add(n.tileId);

                    if (tileIndex > 0)
                    {
                        // Next tile toward destination
                        PlanetTile candidateNext = path.NodesReversed[tileIndex - 1];
                        if (neighborIds.Contains(candidateNext.tileId))
                        {
                            nextTileOnPath = candidateNext;
                        }
                        else
                        {
                            // Search for a neighbor in the path going toward destination
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

                    // If at segment end, look at next path
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

            // Use arrow key direction for consistency with navigation
            return WorldInfoHelper.GetArrowKeyDirection(fromTile, nextTileOnPath);
        }

        /// <summary>
        /// Estimates ticks from start to the specified tile on the path.
        /// </summary>
        private static int EstimateTicksToTile(PlanetTile tile, WorldPath path, int pathIndex, WorldRoutePlanner planner)
        {
            // Get base time to the start of this path segment
            int ticksToPathStart = planner.GetTicksToWaypoint(pathIndex);

            // Estimate position within path
            int tileIndex = path.NodesReversed.IndexOf(tile);
            int totalNodes = path.NodesReversed.Count;
            if (totalNodes <= 1 || tileIndex < 0)
                return ticksToPathStart;

            // Path goes from index (count-1) to index 0
            // tileIndex 0 = at destination end of this segment
            // tileIndex (count-1) = at start of this segment
            float progress = 1f - ((float)tileIndex / (totalNodes - 1));

            int ticksForThisSegment = planner.GetTicksToWaypoint(pathIndex + 1) - ticksToPathStart;
            return ticksToPathStart + (int)(ticksForThisSegment * progress);
        }

        /// <summary>
        /// Gets a short route status announcement for the current tile.
        /// Lists all path segments through this tile (for routes that double back).
        /// Returns null if not on route or route planner inactive.
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

            // Check if at final destination
            if (planner.waypoints[planner.waypoints.Count - 1].Tile.tileId == tileId)
            {
                return "Destination.";
            }

            // Find ALL path segments that contain this tile and get direction for each
            List<string> pathDescriptions = new List<string>();

            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                WorldPath path = paths[pathIndex];
                if (path == null || !path.Found)
                    continue;

                // Check if this tile is on this path segment
                bool onThisPath = false;
                for (int j = 0; j < path.NodesReversed.Count; j++)
                {
                    if (path.NodesReversed[j].tileId == tileId)
                    {
                        onThisPath = true;
                        break;
                    }
                }

                // Also check if it's the starting waypoint of this segment
                if (!onThisPath && planner.waypoints[pathIndex].Tile.tileId == tileId)
                {
                    onThisPath = true;
                }

                if (onThisPath)
                {
                    // Check if we're at the destination of this path segment (don't show "To waypoint X" if we're already at X)
                    int targetWaypointIndex = pathIndex + 1;
                    if (targetWaypointIndex < planner.waypoints.Count &&
                        planner.waypoints[targetWaypointIndex].Tile.tileId == tileId)
                    {
                        // We're at the destination of this path - skip it
                        continue;
                    }

                    // Get direction for this specific path segment
                    string direction = GetDirectionForPathSegment(tile, path, pathIndex, paths, planner);
                    if (!string.IsNullOrEmpty(direction))
                    {
                        int targetWaypoint = pathIndex + 2; // paths[0] goes to waypoint 2, etc.
                        pathDescriptions.Add($"To waypoint {targetWaypoint}: {direction}");
                    }
                }
            }

            if (pathDescriptions.Count == 0)
                return null;

            // Build conversational format: "Head north to waypoint 2, south to waypoint 4."
            // Extract direction and waypoint from each "To waypoint X: direction" entry
            List<string> parts = new List<string>();
            foreach (string desc in pathDescriptions)
            {
                var match = System.Text.RegularExpressions.Regex.Match(desc, @"To waypoint (\d+): (.+)");
                if (match.Success)
                {
                    string waypointNum = match.Groups[1].Value;
                    string direction = match.Groups[2].Value;
                    parts.Add($"{direction} to waypoint {waypointNum}");
                }
            }

            if (parts.Count == 0)
                return null;

            // Join with commas: "Head north to waypoint 2, south to waypoint 4."
            return $"Head {string.Join(", ", parts)}.";
        }

        /// <summary>
        /// Gets the compass direction for a specific path segment from the given tile.
        /// </summary>
        private static string GetDirectionForPathSegment(PlanetTile fromTile, WorldPath path, int pathIndex, List<WorldPath> paths, WorldRoutePlanner planner)
        {
            if (!fromTile.Valid || Find.WorldGrid == null)
                return null;

            int fromTileId = fromTile.tileId;

            // Find tile in this path
            int tileIndex = -1;
            for (int j = 0; j < path.NodesReversed.Count; j++)
            {
                if (path.NodesReversed[j].tileId == fromTileId)
                {
                    tileIndex = j;
                    break;
                }
            }

            // Get neighbors
            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(fromTile, neighbors);
            HashSet<int> neighborIds = new HashSet<int>();
            foreach (var n in neighbors)
                neighborIds.Add(n.tileId);

            PlanetTile nextTile = PlanetTile.Invalid;

            if (tileIndex > 0)
            {
                // Next tile toward destination in this segment
                PlanetTile candidate = path.NodesReversed[tileIndex - 1];
                if (neighborIds.Contains(candidate.tileId))
                    nextTile = candidate;
            }
            else if (tileIndex == 0 || tileIndex == -1)
            {
                // At end of this segment or at the starting waypoint - look at next path
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

            // Use arrow key direction for consistency with navigation
            return WorldInfoHelper.GetArrowKeyDirection(fromTile, nextTile);
        }

        /// <summary>
        /// Checks if the user has moved off the route and announces "Off route" if so.
        /// Also tracks which path segment the user is on (for overlapping paths).
        /// Should be called after tile changes when route planner is active.
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

            // Track which path segment we're on by detecting when we reach waypoints
            // This is crucial for routes that overlap (e.g., A→B→A)
            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                if (planner.waypoints[i].Tile.tileId == currentTileId)
                {
                    // We're at waypoint i
                    if (currentTileId != lastWaypointTileId)
                    {
                        // Just arrived at this waypoint
                        lastWaypointTileId = currentTileId;

                        // Update current path segment:
                        // At waypoint i, we should be on path segment i (heading toward waypoint i+1)
                        // Exception: at the final waypoint, stay on the last segment
                        if (i < planner.waypoints.Count - 1)
                        {
                            currentPathSegment = i;
                        }
                    }
                    break;
                }
            }

            // If we're not at a waypoint, clear the last waypoint tracker
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

            // Note: We don't announce "Off route" because hex grid navigation often requires
            // stepping off the computed path temporarily (e.g., when the path goes diagonally
            // but arrow keys only move in 4 cardinal directions)

            wasOnRoute = nowOnRoute;
        }

        /// <summary>
        /// Gets timing info for the current path segment.
        /// Returns (nextWaypointNumber, ticksFromStart) or null if not on route.
        /// ticksFromStart is the cumulative time from waypoint 1 to reach this tile (matches game behavior).
        /// Uses currentPathSegment to determine which waypoint we're heading toward.
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

            // Target waypoint is currentPathSegment + 1 (segment 0 goes to waypoint 1, etc.)
            int targetWaypointIndex = currentPathSegment + 1;
            if (targetWaypointIndex >= planner.waypoints.Count)
                return null;

            // Get time to start of current segment (cumulative from waypoint 1)
            int ticksToSegmentStart = planner.GetTicksToWaypoint(currentPathSegment);

            // Get total time for this segment
            int ticksToTargetWaypoint = planner.GetTicksToWaypoint(targetWaypointIndex);
            int segmentTicks = ticksToTargetWaypoint - ticksToSegmentStart;

            // Estimate progress within current segment based on tile position
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
                // Tile is on the current path segment - estimate time from start
                int totalNodes = currentPath.NodesReversed.Count;
                // NodesReversed: index 0 = destination, higher index = toward start
                // Progress through segment = 1 - (tileIndex / (totalNodes - 1))
                // At destination (index 0): progress = 1.0
                // At start (index totalNodes-1): progress = 0.0
                float progressFraction = (totalNodes > 1) ? 1f - ((float)tileIndex / (totalNodes - 1)) : 1f;
                ticksFromStart = ticksToSegmentStart + (int)(segmentTicks * progressFraction);
            }
            else
            {
                // Tile not on current segment (maybe off-route or at waypoint)
                // Just return time to segment start
                ticksFromStart = ticksToSegmentStart;
            }

            // waypointNumber is 1-based for display (waypoint 2, 3, etc.)
            return (targetWaypointIndex + 1, ticksFromStart);
        }

        /// <summary>
        /// Resets the route tracking state.
        /// Called when route planner is opened or closed.
        /// </summary>
        public static void ResetRouteTracking()
        {
            wasOnRoute = false;
            currentPathSegment = 0;
            lastWaypointTileId = -1;
        }

        #endregion
    }
}
