using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for transport pod world map targeting.
    /// Tracks when launch targeting is active and provides fuel cost calculations.
    /// Works with WorldScannerState to announce fuel costs for each destination.
    /// </summary>
    public static class TransportPodLaunchState
    {
        private static bool isActive = false;
        private static CompLaunchable currentLaunchable = null;
        private static float cachedFuelLevel = 0f;
        private static float cachedMaxRange = 0f;
        private static bool isConfirmingDestination = false;
        private static PlanetTile cachedOriginTile = PlanetTile.Invalid;

        // Single-BFS cache: all tiles reachable within maxRange, with their traversal distances.
        // Built once on Open(), used for O(1) reachability checks and distance lookups.
        // Keyed by tile ID (int) rather than PlanetTile to avoid layer mismatch issues
        // (biome center tiles use layer -1, but BFS produces tiles on the actual layer).
        private static Dictionary<int, int> reachableTileDistances = null;

        /// <summary>
        /// Gets the origin tile for this launch.
        /// </summary>
        public static PlanetTile OriginTile => cachedOriginTile;

        /// <summary>
        /// Gets whether launch targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether we're in the process of confirming a destination.
        /// Used by DialogInterceptionPatch to intercept FloatMenus for arrival options.
        /// </summary>
        public static bool IsConfirmingDestination => isConfirmingDestination;

        /// <summary>
        /// Gets the available fuel level for the current launch.
        /// </summary>
        public static float AvailableFuel => cachedFuelLevel;

        /// <summary>
        /// Gets the maximum launch distance at current fuel level.
        /// </summary>
        public static float MaxRange => cachedMaxRange;

        /// <summary>
        /// Opens launch targeting state when StartChoosingDestination is called.
        /// </summary>
        public static void Open(CompLaunchable launchable)
        {
            if (launchable == null)
            {
                Log.Warning("RimWorld Access: TransportPodLaunchState.Open called with null launchable");
                return;
            }

            currentLaunchable = launchable;
            isActive = true;

            // Cache fuel info
            cachedFuelLevel = TransportPodHelper.GetFuelLevel(launchable);
            cachedMaxRange = TransportPodHelper.GetMaxLaunchDistance(launchable);

            // Cache origin tile from the launchable's map
            if (launchable.parent?.Map != null)
                cachedOriginTile = launchable.parent.Map.Tile;
            else
                cachedOriginTile = PlanetTile.Invalid;

            BuildReachableTileCache();

            TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenWithFuel".Translate(cachedFuelLevel.ToString("F0"), cachedMaxRange.ToString("F0")));
        }

        /// <summary>
        /// Opens launch targeting state for shuttles or other non-CompLaunchable launches.
        /// Used when WorldTargeter.BeginTargeting is called directly (e.g., permit shuttles).
        /// </summary>
        public static void Open(PlanetTile originTile, int maxLaunchDistance)
        {
            currentLaunchable = null;
            isActive = true;
            cachedFuelLevel = -1f; // Sentinel: no per-tile fuel tracking
            cachedMaxRange = maxLaunchDistance;
            cachedOriginTile = originTile;

            BuildReachableTileCache();

            if (maxLaunchDistance > 0)
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenWithRange".Translate(maxLaunchDistance));
            else
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenUnlimited".Translate());
        }

        /// <summary>
        /// Performs a single BFS FloodFill on the Surface layer, collecting all tiles
        /// within maxRange along with their traversal distances.
        /// Built on Surface because landing destinations are always surface tiles.
        /// If the origin is on a different layer (e.g., orbit), converts to its
        /// surface equivalent first, matching the game's cross-layer handling.
        /// </summary>
        private static void BuildReachableTileCache()
        {
            reachableTileDistances = new Dictionary<int, int>();

            if (!cachedOriginTile.Valid || cachedMaxRange <= 0 || Find.WorldGrid == null)
                return;

            int maxRange = (int)cachedMaxRange;

            // Build BFS on Surface layer — landing destinations are always on the surface.
            PlanetLayer surface = Find.WorldGrid.Surface;
            PlanetTile surfaceOrigin = (cachedOriginTile.Layer == surface)
                ? cachedOriginTile
                : surface.GetClosestTile_NewTemp(cachedOriginTile);

            if (!surfaceOrigin.Valid)
                return;

            int maxTiles = Find.WorldGrid.TilesNumWithinTraversalDistance(maxRange + 1);

            surface.Filler.FloodFill(
                surfaceOrigin,
                (PlanetTile tile) => true,
                (PlanetTile tile, int dist) =>
                {
                    if (dist > maxRange)
                        return true;
                    reachableTileDistances[(int)tile] = dist;
                    return false;
                },
                maxTiles);
        }

        /// <summary>
        /// Closes launch targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentLaunchable = null;
            cachedFuelLevel = 0f;
            cachedMaxRange = 0f;
            cachedOriginTile = PlanetTile.Invalid;
            isConfirmingDestination = false;
            reachableTileDistances = null;
        }

        /// <summary>
        /// Clears the confirming destination flag.
        /// Called after the float menu has been processed.
        /// </summary>
        public static void ClearConfirmingFlag()
        {
            isConfirmingDestination = false;
        }

        /// <summary>
        /// Calculates the fuel cost to reach a destination at the given distance.
        /// </summary>
        public static float CalculateFuelCost(float distanceInTiles)
        {
            if (currentLaunchable == null)
                return float.MaxValue;

            return TransportPodHelper.CalculateFuelCost(currentLaunchable, distanceInTiles);
        }

        /// <summary>
        /// Checks if a destination at the given approximate distance is reachable.
        /// Uses approximate distance for fast checks (scanner filtering).
        /// Note: ApproxDistanceInTiles underestimates vs the game's TraversalDistanceBetween,
        /// so this may include some tiles that are actually out of range.
        /// </summary>
        public static bool CanReachDistance(float distanceInTiles)
        {
            return distanceInTiles <= cachedMaxRange;
        }

        /// <summary>
        /// Checks if a destination tile is reachable, using the pre-built BFS cache.
        /// O(1) lookup — no pathfinding per call.
        /// </summary>
        public static bool CanReachTile(PlanetTile destination)
        {
            if (reachableTileDistances == null || cachedMaxRange <= 0)
                return true; // No range limit or cache not built

            return reachableTileDistances.ContainsKey((int)destination);
        }

        /// <summary>
        /// Gets the BFS traversal distance for a tile from the cache.
        /// Returns -1 if the tile is not in the reachable cache.
        /// </summary>
        public static int GetCachedDistance(PlanetTile tile)
        {
            if (reachableTileDistances != null && reachableTileDistances.TryGetValue((int)tile, out int dist))
                return dist;
            return -1;
        }

        /// <summary>
        /// Gets the traversal distance (actual tile hops) to a destination from the BFS cache.
        /// Falls back to TraversalDistanceBetween if the tile wasn't in the cache
        /// (e.g., for tiles beyond max range that the user navigated to directly).
        /// </summary>
        public static int GetTraversalDistance(PlanetTile destination)
        {
            if (!cachedOriginTile.Valid || !destination.Valid)
                return 0;

            // Fast path: lookup from BFS cache
            if (reachableTileDistances != null && reachableTileDistances.TryGetValue((int)destination, out int dist))
                return dist;

            // Slow path: tile not in cache (beyond range), compute individually
            return Find.WorldGrid.TraversalDistanceBetween(
                cachedOriginTile, destination, passImpassable: true, int.MaxValue, canTraverseLayers: true);
        }

        /// <summary>
        /// Builds a fuel cost announcement for a destination.
        /// </summary>
        public static string GetFuelCostAnnouncement(float distanceInTiles)
        {
            if (!isActive)
                return "";

            // For transport pods with CompLaunchable, use detailed fuel cost calculation
            if (currentLaunchable != null)
                return TransportPodHelper.BuildFuelCostAnnouncement(currentLaunchable, distanceInTiles);

            // For shuttles (no CompLaunchable), use approximate distance for quick check
            // Note: accurate range checking uses CanReachTile with traversal distance
            if (cachedMaxRange > 0 && distanceInTiles > cachedMaxRange)
                return (string)"RimWorldAccess.TransportPods.Fuel.OutOfRange".Translate();

            return "";
        }

        /// <summary>
        /// Gets fuel cost announcement using traversal distance.
        /// Uses the game's own FuelNeededToLaunchAtDist for transport pods,
        /// and traversal distance vs max range for shuttles.
        /// </summary>
        public static string GetFuelCostAnnouncementForTile(PlanetTile destination)
        {
            if (!isActive || !cachedOriginTile.Valid || !destination.Valid)
                return "";

            // Use BFS cache for traversal distance (O(1) lookup)
            int traversalDist = GetTraversalDistance(destination);

            // For transport pods with CompLaunchable, use the game's own fuel cost method
            if (currentLaunchable != null)
            {
                float fuelNeeded = currentLaunchable.FuelNeededToLaunchAtDist(traversalDist, destination.Layer);
                if (fuelNeeded > cachedFuelLevel)
                    return (string)"RimWorldAccess.TransportPods.Fuel.NotEnough".Translate(fuelNeeded.ToString("F0"), cachedFuelLevel.ToString("F0"));
                return (string)"RimWorldAccess.TransportPods.Fuel.CostChemfuel".Translate(fuelNeeded.ToString("F0"));
            }

            // For shuttles (no CompLaunchable), check traversal distance vs max range
            if (cachedMaxRange > 0 && traversalDist > (int)cachedMaxRange)
                return (string)"RimWorldAccess.TransportPods.Fuel.OutOfRange".Translate();

            return "";
        }

        /// <summary>
        /// Gets the origin tile index for distance calculations.
        /// </summary>
        public static int GetOriginTile()
        {
            // Use cached origin tile (works for both CompLaunchable and shuttle launches)
            if (cachedOriginTile.Valid)
                return cachedOriginTile;

            // Fallback to CompLaunchable's map tile
            if (currentLaunchable?.parent?.Map == null)
                return -1;

            return currentLaunchable.parent.Map.Tile;
        }

        /// <summary>
        /// Handles keyboard input during launch targeting.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // If WorldTargeter stopped (e.g., after successful launch), close our state
            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                Close();
                return false;
            }

            // Enter - confirm current destination
            // But skip if WindowlessFloatMenuState is active (arrival options menu is showing)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                // If the float menu is showing arrival options, let it handle Enter
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                ConfirmCurrentDestination();
                return true;
            }

            // Escape - cancel targeting
            // But skip if WindowlessFloatMenuState is active (let it close first)
            if (key == KeyCode.Escape)
            {
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                CancelTargeting();
                return true;
            }

            // F - announce fuel status
            if (key == KeyCode.F && !shift && !ctrl && !alt)
            {
                AnnounceFuelStatus();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Confirms the currently selected world tile as the destination.
        /// Uses reflection to invoke the WorldTargeter's action callback, which triggers
        /// the game's arrival options logic (and may create a FloatMenu).
        /// </summary>
        private static void ConfirmCurrentDestination()
        {
            if (!GuardHelper.RequireWorldNav(SpeechPriority.High)) return;

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(selectedTile, SpeechPriority.High)) return;

            // No pre-check for range — let the game's own ChoseWorldTarget handle validation.
            // It uses TraversalDistanceBetween and shows proper messages like "Beyond maximum range".

            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.WorldTargeterNotActive".Translate(), SpeechPriority.High);
                return;
            }

            // Set flag so DialogInterceptionPatch knows to intercept any FloatMenu
            isConfirmingDestination = true;

            try
            {
                // Get the action field from WorldTargeter using reflection
                var actionField = typeof(WorldTargeter).GetField("action", BindingFlags.NonPublic | BindingFlags.Instance);
                if (actionField == null)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.CannotAccessAction".Translate(), SpeechPriority.High);
                    isConfirmingDestination = false;
                    return;
                }

                var action = actionField.GetValue(Find.WorldTargeter) as Func<GlobalTargetInfo, bool>;
                if (action == null)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.NoActionAvailable".Translate(), SpeechPriority.High);
                    isConfirmingDestination = false;
                    return;
                }

                // Create GlobalTargetInfo for the selected tile
                // Check for world objects at the tile first (like settlements)
                GlobalTargetInfo targetInfo;
                var worldObjects = Find.WorldObjects?.ObjectsAt(selectedTile)?.ToList();
                if (worldObjects != null && worldObjects.Count > 0)
                {
                    // Use the first world object (typically a settlement or site)
                    targetInfo = new GlobalTargetInfo(worldObjects[0]);
                }
                else
                {
                    // Just a tile with no world object
                    targetInfo = new GlobalTargetInfo(selectedTile);
                }

                // Invoke the action callback
                // If it returns true, targeting is complete (single option was executed)
                // If it returns false, a FloatMenu was likely created for multiple options
                bool completed = action(targetInfo);

                if (completed)
                {
                    // The action was auto-executed (single option).
                    // BUT: it may have opened a confirmation dialog (e.g., hostile settlement warning).
                    // If a dialog was opened, don't stop targeting — the user might cancel the dialog
                    // and want to pick a different destination.
                    if (WindowlessDialogState.IsActive)
                    {
                        // A confirmation dialog was opened — let it handle the outcome.
                        // If confirmed: the launch action runs and targeting ends naturally.
                        // If cancelled: we stay in targeting mode.
                        isConfirmingDestination = false;
                    }
                    else
                    {
                        // No dialog — action completed immediately (e.g., form caravan on empty tile)
                        Find.WorldTargeter.StopTargeting();
                        isConfirmingDestination = false;
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.TargetSelected".Translate(), SpeechPriority.Normal);
                    }
                }
                else
                {
                    // Multiple options available - FloatMenu should have been intercepted
                    // The flag will be cleared when the menu is processed
                    int traversalDist = GetTraversalDistance(selectedTile);
                    string fuelInfo = GetFuelCostAnnouncementForTile(selectedTile);
                    if (!string.IsNullOrEmpty(fuelInfo))
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.DestinationWithFuel".Translate(traversalDist, fuelInfo));
                    else
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.DestinationBare".Translate(traversalDist));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error confirming destination: {ex}");
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.ErrorSelecting".Translate(), SpeechPriority.High);
                isConfirmingDestination = false;
            }
        }

        /// <summary>
        /// Cancels launch targeting and returns to map.
        /// </summary>
        private static void CancelTargeting()
        {
            // Cache the return target BEFORE closing state (Close() nulls currentLaunchable)
            Thing returnTarget = currentLaunchable?.parent;
            Map returnMap = returnTarget?.Map;

            // For shuttle launches without CompLaunchable, try to return to the current map
            if (returnMap == null)
                returnMap = Find.CurrentMap;

            // Stop world targeting
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
            {
                Find.WorldTargeter.StopTargeting();
            }

            // Close our state
            Close();
            TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.Cancelled".Translate(), SpeechPriority.Normal);

            // Return to map view
            if (returnTarget != null)
            {
                CameraJumper.TryJump(returnTarget);
            }
            else if (returnMap != null)
            {
                CameraJumper.TryHideWorld();
            }
        }

        /// <summary>
        /// Announces the current fuel status.
        /// </summary>
        private static void AnnounceFuelStatus()
        {
            if (currentLaunchable != null)
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusPod".Translate(cachedFuelLevel.ToString("F0"), cachedMaxRange.ToString("F0")), SpeechPriority.Normal);
            else if (cachedMaxRange > 0)
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusRange".Translate(cachedMaxRange.ToString("F0")), SpeechPriority.Normal);
            else
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusUnlimited".Translate(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Called by WorldScannerState to check if fuel costs should be announced.
        /// </summary>
        public static bool ShouldAnnounceFuelCosts()
        {
            return isActive;
        }

        /// <summary>
        /// Called by WorldNavigationPatch when world targeting ends.
        /// </summary>
        public static void OnWorldTargetingEnded()
        {
            if (isActive)
            {
                Close();
            }
        }
    }
}
