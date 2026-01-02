using System;
using System.Linq;
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
                TolkHelper.Speak("World navigation must be active", SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("Route planner not available", SpeechPriority.High);
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
            if (!currentTile.Valid)
            {
                TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                return;
            }

            // Start the route planner
            planner.Start(currentTile.Layer);

            // Use game's localized string for instructions
            // "RoutePlannerPressRMBToAddAndRemoveWaypoints" is visual-specific, so we provide our own
            string instructions = "Route planner active. Space to add waypoint, Shift+Space to remove waypoint at cursor, E for travel time, Escape to exit.";
            TolkHelper.Speak(instructions, SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens the route planner for caravan formation.
        /// The first waypoint is locked to the caravan's starting location.
        /// </summary>
        public static void OpenForCaravan(Dialog_FormCaravan formCaravanDialog)
        {
            if (formCaravanDialog == null)
            {
                TolkHelper.Speak("No caravan dialog", SpeechPriority.High);
                return;
            }

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null)
            {
                TolkHelper.Speak("Route planner not available", SpeechPriority.High);
                return;
            }

            // Start route planner in caravan formation mode
            // This locks the first waypoint to the caravan's starting tile
            planner.Start(formCaravanDialog);

            // Use localized string where appropriate
            string addWaypointsPrompt = "RoutePlannerAddOneOrMoreWaypoints".Translate();
            TolkHelper.Speak($"Choosing route. {addWaypointsPrompt} Space to add waypoint, E for travel time, Enter to confirm, Escape to cancel.", SpeechPriority.Normal);
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
            TolkHelper.Speak("Route planner closed", SpeechPriority.Normal);
        }

        /// <summary>
        /// Adds a waypoint at the current world navigation tile.
        /// </summary>
        public static void AddWaypoint()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("Route planner not active", SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!currentTile.Valid)
            {
                TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                return;
            }

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
                    TolkHelper.Speak($"Waypoint {countAfter} added. Estimated travel time: {timeString} {speedSource.ToLower()}", SpeechPriority.Normal);
                }
                else
                {
                    // First waypoint (starting point)
                    TolkHelper.Speak($"Waypoint {countAfter} added (starting point)", SpeechPriority.Normal);
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
                TolkHelper.Speak("Route planner not active", SpeechPriority.High);
                return;
            }

            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
            if (!currentTile.Valid)
            {
                TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                return;
            }

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
                TolkHelper.Speak("No waypoint here to remove", SpeechPriority.Normal);
                return;
            }

            // TryRemoveWaypoint handles validation (can't remove first in caravan mode) and shows messages
            int countBefore = planner.waypoints.Count;
            planner.TryRemoveWaypoint(waypointAtCursor, playSound: true);
            int countAfter = planner.waypoints.Count;

            if (countAfter < countBefore)
            {
                string message = $"Waypoint {waypointIndex + 1} removed. {countAfter} waypoints remaining.";

                // If we still have 2+ waypoints, also announce travel time to final destination
                if (countAfter >= 2)
                {
                    int ticksToFinal = planner.GetTicksToWaypoint(countAfter - 1);
                    string timeString = ticksToFinal.ToStringTicksToDays("0.#");
                    string speedSource = GetSpeedSourceDescription();
                    message += $" Estimated travel time: {timeString} {speedSource.ToLower()}";
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
                TolkHelper.Speak("Route planner not active", SpeechPriority.High);
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
            TolkHelper.Speak($"Estimated travel time: {timeString} {speedSource.ToLower()}", SpeechPriority.Normal);
        }

        /// <summary>
        /// Announces the full route summary.
        /// </summary>
        public static void AnnounceRouteSummary()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("Route planner not active", SpeechPriority.High);
                return;
            }

            int count = planner.waypoints.Count;
            if (count == 0)
            {
                TolkHelper.Speak("No waypoints set", SpeechPriority.Normal);
                return;
            }

            if (count == 1)
            {
                string tileName = WorldInfoHelper.GetTileSummary(planner.waypoints[0].Tile);
                TolkHelper.Speak($"Starting point: {tileName}. Add more waypoints with Space.", SpeechPriority.Normal);
                return;
            }

            // Multiple waypoints - announce total time
            int totalTicks = planner.GetTicksToWaypoint(count - 1);
            string timeString = totalTicks.ToStringTicksToDays("0.#");
            string speedSource = GetSpeedSourceDescription();

            // Use game's localized string
            string totalEta = "RoutePlannerEstTimeToFinalDest".Translate(timeString);
            TolkHelper.Speak($"{count} waypoints. {totalEta} {speedSource.ToLower()}", SpeechPriority.Normal);
        }

        /// <summary>
        /// Confirms the route and returns to caravan formation (if in caravan mode).
        /// </summary>
        public static void ConfirmRoute()
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                TolkHelper.Speak("Route planner not active", SpeechPriority.High);
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
            string destName = WorldInfoHelper.GetTileSummary(destination);

            // The route planner will handle returning to the dialog when Stop() is called
            // if currentFormCaravanDialog is set
            TolkHelper.Speak($"Route confirmed. Destination: {destName}", SpeechPriority.Normal);

            // Trigger the accept action - this will close route planner and return to dialog
            // We use reflection to access currentFormCaravanDialog and call Notify_ChoseRoute
            try
            {
                var dialogField = HarmonyLib.AccessTools.Field(typeof(WorldRoutePlanner), "currentFormCaravanDialog");
                var dialog = dialogField?.GetValue(planner) as Dialog_FormCaravan;

                if (dialog != null)
                {
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
    }
}
