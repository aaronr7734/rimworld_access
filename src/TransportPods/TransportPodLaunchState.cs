using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Launch-targeting session state for transport pods and shuttles on the world map,
    /// including the fuel costs WorldScannerState announces per destination.
    /// </summary>
    public static class TransportPodLaunchState
    {
        private static bool isActive = false;
        private static CompLaunchable currentLaunchable = null;
        private static float cachedFuelLevel = 0f;
        private static float cachedMaxRange = 0f;
        private static bool isConfirmingDestination = false;
        private static PlanetTile cachedOriginTile = PlanetTile.Invalid;

        // Every tile reachable within maxRange with its traversal distance, built once on
        // Open(). Keyed by tile ID, not PlanetTile: biome center tiles carry layer -1 while
        // the BFS produces tiles on the real layer, so PlanetTile keys would miss.
        private static Dictionary<int, int> reachableTileDistances = null;

        /// <summary>The tile this launch departs from.</summary>
        public static PlanetTile OriginTile => cachedOriginTile;

        /// <summary>Whether launch targeting is active.</summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Whether a destination confirmation is in flight; DialogInterceptionPatch reads this
        /// to intercept the arrival-options FloatMenu.
        /// </summary>
        public static bool IsConfirmingDestination => isConfirmingDestination;

        /// <summary>Fuel available for this launch.</summary>
        public static float AvailableFuel => cachedFuelLevel;

        /// <summary>Maximum launch distance at the current fuel level.</summary>
        public static float MaxRange => cachedMaxRange;

        /// <summary>
        /// Opens the session for a CompLaunchable's StartChoosingDestination. The fuel is what
        /// vanilla's launch gate uses: the override when one is passed, else the group's emptiest pod.
        /// </summary>
        public static void Open(CompLaunchable launchable, float? overrideFuelLevel = null)
        {
            if (launchable == null)
            {
                Log.Warning("RimWorld Access: TransportPodLaunchState.Open called with null launchable");
                return;
            }

            currentLaunchable = launchable;
            isActive = true;

            cachedFuelLevel = overrideFuelLevel ?? TransportPodHelper.GetGroupFuelLevel(launchable);
            cachedMaxRange = launchable.MaxLaunchDistanceAtFuelLevel(cachedFuelLevel);

            // Thing.Tile walks ParentHolder, so a caravan-held shuttle (parent.Map null) still
            // resolves. Without it the reachability cache stays empty and FilterToReachableItems
            // strips every scanner destination.
            cachedOriginTile = launchable.parent?.Tile ?? PlanetTile.Invalid;

            BuildReachableTileCache();

            TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenWithFuel".Loc(cachedFuelLevel.ToString("F0"), cachedMaxRange.ToString("F0")));
        }

        /// <summary>
        /// Opens the session for a non-CompLaunchable launch, where WorldTargeter.BeginTargeting
        /// is called directly.
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
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenWithRange".Loc(maxLaunchDistance));
            else
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.OpenUnlimited".Loc());
        }

        /// <summary>
        /// One BFS FloodFill collecting every tile within maxRange and its traversal distance.
        /// Runs on the Surface layer, since landing destinations are always surface tiles; an
        /// origin on another layer is converted first, as the game does.
        /// </summary>
        private static void BuildReachableTileCache()
        {
            reachableTileDistances = new Dictionary<int, int>();

            if (!cachedOriginTile.Valid || cachedMaxRange <= 0 || Find.WorldGrid == null)
                return;

            int maxRange = (int)cachedMaxRange;

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

        /// <summary>Closes the session and drops the reachability cache.</summary>
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

        /// <summary>Clears the confirming flag once the float menu has been processed.</summary>
        public static void ClearConfirmingFlag()
        {
            isConfirmingDestination = false;
        }

        /// <summary>Fuel cost to reach a destination at the given distance.</summary>
        public static float CalculateFuelCost(float distanceInTiles)
        {
            if (currentLaunchable == null)
                return float.MaxValue;

            return TransportPodHelper.CalculateFuelCost(currentLaunchable, distanceInTiles);
        }

        /// <summary>
        /// Fast reachability check for scanner filtering. ApproxDistanceInTiles underestimates
        /// TraversalDistanceBetween, so this admits some tiles that are truly out of range.
        /// </summary>
        public static bool CanReachDistance(float distanceInTiles)
        {
            return distanceInTiles <= cachedMaxRange;
        }

        /// <summary>Reachability from the BFS cache: an O(1) lookup, no pathfinding.</summary>
        public static bool CanReachTile(PlanetTile destination)
        {
            if (reachableTileDistances == null || cachedMaxRange <= 0)
                return true; // No range limit or cache not built

            return reachableTileDistances.ContainsKey((int)destination);
        }

        /// <summary>Cached BFS traversal distance for a tile, or -1 when it is not cached.</summary>
        public static int GetCachedDistance(PlanetTile tile)
        {
            if (reachableTileDistances != null && reachableTileDistances.TryGetValue((int)tile, out int dist))
                return dist;
            return -1;
        }

        /// <summary>
        /// Traversal distance in tile hops, from the cache, falling back to
        /// TraversalDistanceBetween for tiles beyond max range that the user navigated to.
        /// </summary>
        public static int GetTraversalDistance(PlanetTile destination)
        {
            if (!cachedOriginTile.Valid || !destination.Valid)
                return 0;

            if (reachableTileDistances != null && reachableTileDistances.TryGetValue((int)destination, out int dist))
                return dist;

            return Find.WorldGrid.TraversalDistanceBetween(
                cachedOriginTile, destination, passImpassable: true, int.MaxValue, canTraverseLayers: true);
        }

        /// <summary>The fuel-cost announcement for a destination.</summary>
        public static string GetFuelCostAnnouncement(float distanceInTiles)
        {
            if (!isActive)
                return "";

            if (currentLaunchable != null)
                return TransportPodHelper.BuildFuelCostAnnouncement(currentLaunchable, distanceInTiles);

            // A shuttle has no CompLaunchable; CanReachTile does the accurate range check.
            if (cachedMaxRange > 0 && distanceInTiles > cachedMaxRange)
                return (string)"RimWorldAccess.TransportPods.Fuel.OutOfRange".Translate();

            return "";
        }

        /// <summary>
        /// The fuel-cost announcement from traversal distance: the game's own
        /// FuelNeededToLaunchAtDist for pods, distance against max range for shuttles.
        /// </summary>
        public static string GetFuelCostAnnouncementForTile(PlanetTile destination)
        {
            if (!isActive || !cachedOriginTile.Valid || !destination.Valid)
                return "";

            int traversalDist = GetTraversalDistance(destination);

            if (currentLaunchable != null)
            {
                float fuelNeeded = currentLaunchable.FuelNeededToLaunchAtDist(traversalDist, destination.Layer);
                if (fuelNeeded > cachedFuelLevel)
                    return (string)"RimWorldAccess.TransportPods.Fuel.NotEnough".Translate(fuelNeeded.ToString("F0"), cachedFuelLevel.ToString("F0"));
                return (string)"RimWorldAccess.TransportPods.Fuel.CostChemfuel".Translate(fuelNeeded.ToString("F0"));
            }

            if (cachedMaxRange > 0 && traversalDist > (int)cachedMaxRange)
                return (string)"RimWorldAccess.TransportPods.Fuel.OutOfRange".Translate();

            return "";
        }

        /// <summary>Origin tile index for distance calculations.</summary>
        public static int GetOriginTile()
        {
            if (cachedOriginTile.Valid)
                return cachedOriginTile;

            if (currentLaunchable?.parent?.Map == null)
                return -1;

            return currentLaunchable.parent.Map.Tile;
        }

        /// <summary>
        /// Confirms the selected world tile, invoking WorldTargeter's private action callback
        /// by reflection so the game runs its own arrival-options logic (which may raise a
        /// FloatMenu). No float-menu guard is needed here: ShellDispatcherPatch's
        /// LegacyKeyboardOverlayActive stand-down already stops the shell dispatching.
        /// </summary>
        internal static void ConfirmCurrentDestination()
        {
            if (!GuardHelper.RequireWorldNav(SpeechPriority.High)) return;

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!GuardHelper.RequireValidTile(selectedTile, SpeechPriority.High)) return;

            // No range pre-check: the game's ChoseWorldTarget validates and reports itself.

            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.WorldTargeterNotActive".Loc(), SpeechPriority.High);
                return;
            }

            // Tells DialogInterceptionPatch to intercept whatever FloatMenu this raises.
            isConfirmingDestination = true;

            try
            {
                var actionField = typeof(WorldTargeter).GetField("action", BindingFlags.NonPublic | BindingFlags.Instance);
                if (actionField == null)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.CannotAccessAction".Loc(), SpeechPriority.High);
                    isConfirmingDestination = false;
                    return;
                }

                var action = actionField.GetValue(Find.WorldTargeter) as Func<GlobalTargetInfo, bool>;
                if (action == null)
                {
                    TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.NoActionAvailable".Loc(), SpeechPriority.High);
                    isConfirmingDestination = false;
                    return;
                }

                GlobalTargetInfo targetInfo;
                var worldObjects = Find.WorldObjects?.ObjectsAt(selectedTile)?.ToList();
                if (worldObjects != null && worldObjects.Count > 0)
                {
                    targetInfo = new GlobalTargetInfo(worldObjects[0]);
                }
                else
                {
                    targetInfo = new GlobalTargetInfo(selectedTile);
                }

                // True: the sole arrival option ran. False: a FloatMenu offered several.
                bool completed = action(targetInfo);

                if (completed)
                {
                    // The option may have opened a confirmation dialog. Targeting must stay
                    // open in that case, so a cancel returns the user to picking a destination.
                    if (RimWorldAccess.Shell.FocusStack.AnyLiveModal)
                    {
                        isConfirmingDestination = false;
                    }
                    else
                    {
                        Find.WorldTargeter.StopTargeting();
                        isConfirmingDestination = false;
                        // "Completed" means only that the world action ran: "Land in existing
                        // map" spends its run opening a LOCAL targeter that announces its own
                        // prompt, which "Target selected" would contradict.
                        bool localTargetingStarted = Find.CurrentMap != null
                            && ExternalMapTargeting.MapTargetingActive;
                        if (!localTargetingStarted)
                        {
                            TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.TargetSelected".Loc(), SpeechPriority.Normal);
                        }
                    }
                }
                else
                {
                    // The FloatMenu was intercepted; the flag clears when it is processed.
                    int traversalDist = GetTraversalDistance(selectedTile);
                    string fuelInfo = GetFuelCostAnnouncementForTile(selectedTile);
                    if (!string.IsNullOrEmpty(fuelInfo))
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.DestinationWithFuel".Loc(traversalDist, fuelInfo));
                    else
                        TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.DestinationBare".Loc(traversalDist));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error confirming destination: {ex}");
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.ErrorSelecting".Loc(), SpeechPriority.High);
                isConfirmingDestination = false;
            }
        }

        /// <summary>Cancels launch targeting and returns to the map.</summary>
        internal static void CancelTargeting()
        {
            // Resolve the return target first: Close() nulls currentLaunchable.
            Thing returnTarget = currentLaunchable?.parent;
            Map returnMap = returnTarget?.Map;

            if (returnMap == null)
                returnMap = Find.CurrentMap;

            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
            {
                Find.WorldTargeter.StopTargeting();
            }

            Close();
            TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.Cancelled".Loc(), SpeechPriority.Normal);

            if (returnTarget != null)
            {
                CameraJumper.TryJump(returnTarget);
            }
            else if (returnMap != null)
            {
                CameraJumper.TryHideWorld();
            }
        }

        /// <summary>Announces the current fuel status.</summary>
        internal static void AnnounceFuelStatus()
        {
            if (currentLaunchable != null)
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusPod".Loc(cachedFuelLevel.ToString("F0"), cachedMaxRange.ToString("F0")), SpeechPriority.Normal);
            else if (cachedMaxRange > 0)
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusRange".Loc(cachedMaxRange.ToString("F0")), SpeechPriority.Normal);
            else
                TolkHelper.Speak("RimWorldAccess.TransportPods.Launch.FuelStatusUnlimited".Loc(), SpeechPriority.Normal);
        }

        /// <summary>Whether the scanner should announce fuel costs.</summary>
        public static bool ShouldAnnounceFuelCosts()
        {
            return isActive;
        }

        /// <summary>Hook for WorldNavigationPatch when world targeting ends.</summary>
        public static void OnWorldTargetingEnded()
        {
            if (isActive)
            {
                Close();
            }
        }
    }
}
