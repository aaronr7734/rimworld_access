using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class GravshipDestinationState
    {
        private static bool isActive = false;
        private static bool isLaunching = false;
        private static CompPilotConsole currentConsole = null;
        private static Building_GravEngine currentEngine = null;
        private static float cachedTotalFuel = 0f;
        private static int cachedMaxRange = 0;
        private static float cachedFuelPerTile = 0f;

        // Single-BFS cache: all tiles reachable within maxRange, with traversal distances.
        // Built once on Open(), used for O(1) reachability checks during scanner filtering.
        // Keyed by tile ID (int) to avoid PlanetTile layer mismatch with biome center tiles.
        private static Dictionary<int, int> reachableTileDistances = null;

        // Cached reflection fields for TilePicker
        private static FieldInfo tilePickerValidatorField;
        private static FieldInfo tilePickerTileChosenField;

        public static bool IsActive => isActive;

        public static void Open(CompPilotConsole console, bool launching = true)
        {
            if (console == null) return;

            currentConsole = console;
            isLaunching = launching;

            // Get engine via reflection (engine field is on CompGravshipFacility base class)
            var engineField = AccessTools.Field(typeof(CompGravshipFacility), "engine");
            currentEngine = engineField?.GetValue(console) as Building_GravEngine;

            if (currentEngine == null)
            {
                Log.Warning("[GravshipDestinationState] Could not get engine from pilot console");
                return;
            }

            isActive = true;

            // Cache fuel info
            cachedTotalFuel = currentEngine.TotalFuel;
            cachedMaxRange = currentEngine.MaxLaunchDistance;
            cachedFuelPerTile = currentEngine.FuelPerTile;

            BuildReachableTileCache();

            if (launching)
            {
                TolkHelper.Speak("RimWorldAccess.Gravships.Destination.OpenLaunching".Translate(
                    cachedTotalFuel.ToString("F0"), cachedMaxRange));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Gravships.Destination.OpenViewRange".Translate(
                    cachedTotalFuel.ToString("F0"), cachedMaxRange));
            }
        }

        public static void Close()
        {
            isActive = false;
            isLaunching = false;
            currentConsole = null;
            currentEngine = null;
            cachedTotalFuel = 0f;
            cachedMaxRange = 0;
            cachedFuelPerTile = 0f;
            reachableTileDistances = null;
        }

        /// <summary>
        /// Performs a single BFS FloodFill on the Surface layer, collecting all tiles
        /// within maxRange along with their traversal distances.
        /// Built on Surface because landing destinations are always surface tiles.
        /// If the origin is on a different layer (e.g., orbit), converts to its
        /// surface equivalent first, matching how GravshipUtility.TryGetPathFuelCost works.
        /// </summary>
        private static void BuildReachableTileCache()
        {
            reachableTileDistances = new Dictionary<int, int>();

            if (currentConsole?.parent?.Map == null || cachedMaxRange <= 0 || Find.WorldGrid == null)
                return;

            PlanetTile origin = currentConsole.parent.Map.Tile;
            if (!origin.Valid)
                return;

            // Build BFS on the Surface layer — landing destinations are always on the surface.
            // If origin is on a different layer (e.g., orbit), find its surface equivalent,
            // matching GravshipUtility.TryGetPathFuelCost's cross-layer handling.
            PlanetLayer surface = Find.WorldGrid.Surface;
            PlanetTile surfaceOrigin = (origin.Layer == surface)
                ? origin
                : surface.GetClosestTile_NewTemp(origin);

            if (!surfaceOrigin.Valid)
                return;

            int maxTiles = Find.WorldGrid.TilesNumWithinTraversalDistance(cachedMaxRange + 1);
            surface.Filler.FloodFill(
                surfaceOrigin,
                (PlanetTile tile) => true,
                (PlanetTile tile, int dist) =>
                {
                    if (dist > cachedMaxRange)
                        return true;
                    reachableTileDistances[(int)tile] = dist;
                    return false;
                },
                maxTiles);
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive) return false;

            // If TilePicker stopped, close our state
            if (Find.TilePicker == null || !Find.TilePicker.Active)
            {
                Close();
                return false;
            }

            // Enter - confirm current destination (launch mode) or close (view mode)
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                if (isLaunching)
                {
                    ConfirmCurrentDestination();
                }
                else
                {
                    // View range mode — Enter just closes like Escape
                    CancelTargeting();
                }
                return true;
            }

            // Escape - cancel targeting
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

        private static void ConfirmCurrentDestination()
        {
            if (!GuardHelper.RequireWorldNav(SpeechPriority.High)) return;

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!selectedTile.Valid)
            {
                // Try getting from world selector's single selected object
                WorldObject singleSelected = Find.WorldSelector?.SingleSelectedObject;
                if (singleSelected != null && singleSelected.Tile.Valid)
                {
                    selectedTile = singleSelected.Tile;
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Guard.NoValidTileSelected".Translate(), SpeechPriority.High);
                    return;
                }
            }

            // Use TilePicker's validator and tileChosen callbacks via reflection
            try
            {
                if (tilePickerValidatorField == null)
                    tilePickerValidatorField = AccessTools.Field(typeof(TilePicker), "validator");
                if (tilePickerTileChosenField == null)
                    tilePickerTileChosenField = AccessTools.Field(typeof(TilePicker), "tileChosen");

                var validator = tilePickerValidatorField?.GetValue(Find.TilePicker) as Func<PlanetTile, bool>;
                var tileChosen = tilePickerTileChosenField?.GetValue(Find.TilePicker) as Action<PlanetTile>;

                if (validator == null || tileChosen == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Gravships.Destination.CannotAccessCallbacks".Translate(), SpeechPriority.High);
                    return;
                }

                // Validate the tile (this shows game messages on failure)
                if (!validator(selectedTile))
                {
                    // Validator already showed error message via Messages.Message
                    return;
                }

                // Stop TilePicker internally (without calling noTileChosen)
                var stopIntMethod = AccessTools.Method(typeof(TilePicker), "StopTargetingInt");
                stopIntMethod?.Invoke(Find.TilePicker, null);

                // Invoke the tileChosen callback (this triggers the launch confirmation flow)
                tileChosen(selectedTile);
            }
            catch (Exception ex)
            {
                Log.Warning($"[GravshipDestinationState] Error confirming destination: {ex}");
                TolkHelper.Speak("RimWorldAccess.Gravships.Destination.ErrorSelecting".Translate(), SpeechPriority.High);
            }
        }

        private static void CancelTargeting()
        {
            // Cache return target before closing
            Thing returnTarget = currentConsole?.parent;

            if (Find.TilePicker != null && Find.TilePicker.Active)
            {
                Find.TilePicker.StopTargeting();
            }

            Close();
            TolkHelper.Speak("RimWorldAccess.Gravships.Destination.Cancelled".Translate(), SpeechPriority.Normal);

            // Return to map view
            if (returnTarget != null)
            {
                CameraJumper.TryJump(returnTarget);
            }
        }

        private static void AnnounceFuelStatus()
        {
            if (currentEngine == null) return;

            // Refresh fuel data
            cachedTotalFuel = currentEngine.TotalFuel;

            TolkHelper.Speak(
                "RimWorldAccess.Gravships.Destination.FuelStatus".Translate(
                    cachedTotalFuel.ToString("F0"),
                    currentEngine.MaxFuel.ToString("F0"),
                    cachedMaxRange,
                    cachedFuelPerTile.ToString("F1")),
                SpeechPriority.Normal);
        }

        /// <summary>
        /// Called by WorldNavigationState/WorldScannerState to check if fuel costs should be announced.
        /// </summary>
        public static bool ShouldAnnounceFuelCosts()
        {
            return isActive && currentEngine != null;
        }

        /// <summary>
        /// Gets fuel cost announcement for a world tile at the given distance.
        /// </summary>
        public static string GetFuelCostAnnouncement(PlanetTile destinationTile)
        {
            if (!isActive || currentEngine == null || currentConsole?.parent?.Map == null)
                return "";

            PlanetTile originTile = currentConsole.parent.Map.Tile;

            float cost;
            int distance;
            if (GravshipUtility.TryGetPathFuelCost(originTile, destinationTile, out cost, out distance,
                    10f, currentEngine.FuelUseageFactor))
            {
                if (cost > cachedTotalFuel)
                {
                    return "RimWorldAccess.Gravships.Destination.CostNotEnough".Translate(cost.ToString("F0"));
                }
                if (distance > cachedMaxRange)
                {
                    return "TransportPodDestinationBeyondMaximumRange".Translate();
                }
                return "RimWorldAccess.Gravships.Destination.CostOnly".Translate(cost.ToString("F0"));
            }
            else
            {
                return "CannotLaunchDestination".Translate();
            }
        }

        /// <summary>
        /// Checks if a destination tile is reachable using the pre-built BFS cache.
        /// O(1) lookup — no pathfinding per call. Used for scanner filtering.
        /// Fuel cost is only checked during individual item announcement (GetFuelCostAnnouncement).
        /// </summary>
        public static bool CanReachDestination(PlanetTile destinationTile)
        {
            if (reachableTileDistances == null || cachedMaxRange <= 0)
                return false;

            return reachableTileDistances.ContainsKey((int)destinationTile);
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
        /// Gets the origin tile for distance calculations.
        /// </summary>
        public static PlanetTile GetOriginTile()
        {
            if (currentConsole?.parent?.Map == null)
                return PlanetTile.Invalid;

            return currentConsole.parent.Map.Tile;
        }
    }
}
