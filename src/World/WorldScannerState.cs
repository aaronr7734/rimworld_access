using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents a biome region (contiguous area of the same biome).
    /// </summary>
    public class BiomeRegion
    {
        public PlanetTile CenterTile { get; set; }
        public int TileCount { get; set; }
        public string SizeDescription { get; set; } // e.g., "approximately 75 tiles, 8 across"
        public float Distance { get; set; } // Distance from cursor to center
        internal int[] TileIds { get; set; } // All tile IDs in this region, for launch reachability checks

        public BiomeRegion(PlanetTile centerTile, int tileCount)
        {
            CenterTile = centerTile;
            TileCount = tileCount;
            SizeDescription = $"approximately {tileCount} tiles";
        }

        public BiomeRegion(PlanetTile centerTile, HashSet<int> regionTiles)
        {
            CenterTile = centerTile;
            TileCount = regionTiles.Count;
            TileIds = regionTiles.ToArray();
            SizeDescription = $"approximately {TileCount} tiles";
        }
    }

    /// <summary>
    /// Represents a road segment.
    /// </summary>
    public class RoadSegment
    {
        public PlanetTile CenterTile { get; set; }
        public int TileCount { get; set; }
        public float Distance { get; set; }
        public float Length { get; set; } // Maximum extent of the road segment
        public string SizeDescription { get; set; }
        internal int[] TileIds { get; set; } // All tile IDs in this segment, for launch reachability checks

        public RoadSegment(PlanetTile centerTile, int tileCount)
        {
            CenterTile = centerTile;
            TileCount = tileCount;
            SizeDescription = $"{tileCount} tiles";
        }

        /// <summary>
        /// Constructor that calculates length from all segment tiles.
        /// </summary>
        public RoadSegment(PlanetTile centerTile, HashSet<int> segmentTiles)
        {
            CenterTile = centerTile;
            TileCount = segmentTiles.Count;
            TileIds = segmentTiles.ToArray();
            Length = CalculateLength(segmentTiles);

            // For roads, show length if meaningful
            if (Length >= 3)
            {
                SizeDescription = $"{TileCount} tiles, {Length:F0} long";
            }
            else
            {
                SizeDescription = $"{TileCount} tiles";
            }
        }

        /// <summary>
        /// Calculates the maximum extent of the road segment (its length).
        /// </summary>
        private static float CalculateLength(HashSet<int> segmentTiles)
        {
            if (segmentTiles.Count <= 1 || Find.WorldGrid == null)
                return 0f;

            // For roads, find the endpoints (tiles with fewest neighbors in the segment)
            // and calculate distance between them
            var tilesList = segmentTiles.ToList();
            float maxDist = 0f;

            // Sample tiles for performance
            var tilesToCheck = tilesList;
            if (tilesList.Count > 30)
            {
                var sampled = new List<int>();
                sampled.Add(tilesList[0]);
                sampled.Add(tilesList[tilesList.Count - 1]);
                for (int i = 0; i < tilesList.Count; i += tilesList.Count / 8)
                {
                    sampled.Add(tilesList[i]);
                }
                tilesToCheck = sampled.Distinct().ToList();
            }

            for (int i = 0; i < tilesToCheck.Count; i++)
            {
                for (int j = i + 1; j < tilesToCheck.Count; j++)
                {
                    var tileA = new PlanetTile(tilesToCheck[i], -1);
                    var tileB = new PlanetTile(tilesToCheck[j], -1);
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(tileA, tileB);
                    if (dist > maxDist)
                        maxDist = dist;
                }
            }

            return maxDist;
        }
    }

    /// <summary>
    /// Represents an item in the world scanner.
    /// Can be a single world object, or a "type" with multiple instances (biome regions, road segments).
    /// </summary>
    public class WorldScannerItem
    {
        public WorldObject WorldObject { get; set; }
        public PlanetTile Tile { get; set; }
        public string Label { get; set; }
        public string QuestName { get; set; }
        public Faction Faction { get; set; }

        // For biome/road types with multiple instances
        public List<BiomeRegion> BiomeRegions { get; set; }
        public List<RoadSegment> RoadSegments { get; set; }

        public bool HasInstances => (BiomeRegions != null && BiomeRegions.Count > 1) ||
                                    (RoadSegments != null && RoadSegments.Count > 1);
        public int InstanceCount => BiomeRegions?.Count ?? RoadSegments?.Count ?? 1;

        /// <summary>
        /// Constructor for world objects (settlements, caravans, quest sites, etc.)
        /// </summary>
        public WorldScannerItem(WorldObject worldObject)
        {
            WorldObject = worldObject;
            Tile = worldObject.Tile;
            Faction = worldObject.Faction;
            Label = worldObject.LabelShort ?? worldObject.Label ?? "Unknown";
        }

        /// <summary>
        /// Constructor for biome types with regions.
        /// </summary>
        public WorldScannerItem(string biomeName, List<BiomeRegion> regions)
        {
            Label = biomeName;
            BiomeRegions = regions;
            if (regions.Count > 0)
            {
                Tile = regions[0].CenterTile;
            }
        }

        /// <summary>
        /// Constructor for road types with segments.
        /// </summary>
        public WorldScannerItem(string roadName, List<RoadSegment> segments)
        {
            Label = roadName;
            RoadSegments = segments;
            if (segments.Count > 0)
            {
                Tile = segments[0].CenterTile;
            }
        }

        /// <summary>
        /// Constructor for landmark tiles (not WorldObjects).
        /// </summary>
        public WorldScannerItem(string label, PlanetTile tile)
        {
            Label = label;
            Tile = tile;
        }

        /// <summary>
        /// Gets the tile for a specific instance index.
        /// Used for navigation (jumping to the region center).
        /// </summary>
        public PlanetTile GetTileAtInstance(int instanceIndex)
        {
            if (BiomeRegions != null && instanceIndex < BiomeRegions.Count)
                return BiomeRegions[instanceIndex].CenterTile;
            if (RoadSegments != null && instanceIndex < RoadSegments.Count)
                return RoadSegments[instanceIndex].CenterTile;
            return Tile;
        }

        /// <summary>
        /// Calculates distance from origin tile to this item (or a specific instance).
        /// </summary>
        public float GetDistance(PlanetTile fromTile, int instanceIndex = 0)
        {
            if (!fromTile.Valid || Find.WorldGrid == null)
                return 0f;

            PlanetTile targetTile = GetTileAtInstance(instanceIndex);
            if (!targetTile.Valid)
                return 0f;

            return Find.WorldGrid.ApproxDistanceInTiles(fromTile, targetTile);
        }

        /// <summary>
        /// Gets the compass direction from the origin tile to this item.
        /// </summary>
        public string GetDirectionFrom(PlanetTile fromTile, int instanceIndex = 0)
        {
            PlanetTile targetTile = GetTileAtInstance(instanceIndex);
            if (!fromTile.Valid || !targetTile.Valid || Find.WorldGrid == null)
                return "";

            Vector3 fromPos = Find.WorldGrid.GetTileCenter(fromTile);
            Vector3 toPos = Find.WorldGrid.GetTileCenter(targetTile);
            Vector3 direction = (toPos - fromPos).normalized;

            if (WorldNavigationState.IsInPoleTerritory)
            {
                return GetRelativeDirection(fromPos, direction);
            }

            return GetCompassDirection(fromPos, direction);
        }

        private string GetCompassDirection(Vector3 fromPos, Vector3 direction)
        {
            Vector3 up = fromPos.normalized;
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, up).normalized;

            float dotNorth = Vector3.Dot(flatDir, north);
            float dotEast = Vector3.Dot(flatDir, east);
            double angle = Math.Atan2(dotEast, dotNorth) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle >= 337.5 || angle < 22.5) return "North";
            if (angle >= 22.5 && angle < 67.5) return "Northeast";
            if (angle >= 67.5 && angle < 112.5) return "East";
            if (angle >= 112.5 && angle < 157.5) return "Southeast";
            if (angle >= 157.5 && angle < 202.5) return "South";
            if (angle >= 202.5 && angle < 247.5) return "Southwest";
            if (angle >= 247.5 && angle < 292.5) return "West";
            return "Northwest";
        }

        private string GetRelativeDirection(Vector3 fromPos, Vector3 direction)
        {
            Vector3 up = fromPos.normalized;
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, up).normalized;

            float dotNorth = Vector3.Dot(flatDir, north);
            float dotEast = Vector3.Dot(flatDir, east);
            double angle = Math.Atan2(dotEast, dotNorth) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle >= 337.5 || angle < 22.5) return "Ahead";
            if (angle >= 22.5 && angle < 67.5) return "Ahead-right";
            if (angle >= 67.5 && angle < 112.5) return "Right";
            if (angle >= 112.5 && angle < 157.5) return "Behind-right";
            if (angle >= 157.5 && angle < 202.5) return "Behind";
            if (angle >= 202.5 && angle < 247.5) return "Behind-left";
            if (angle >= 247.5 && angle < 292.5) return "Left";
            return "Ahead-left";
        }
    }

    /// <summary>
    /// Represents a subcategory in the world scanner.
    /// </summary>
    public class WorldScannerSubcategory
    {
        public string Name { get; set; }
        public List<WorldScannerItem> Items { get; set; }

        public WorldScannerSubcategory(string name)
        {
            Name = name;
            Items = new List<WorldScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;
    }

    /// <summary>
    /// Represents a category in the world scanner.
    /// </summary>
    public class WorldScannerCategory
    {
        public string Name { get; set; }
        public List<WorldScannerSubcategory> Subcategories { get; set; }

        public WorldScannerCategory(string name)
        {
            Name = name;
            Subcategories = new List<WorldScannerSubcategory>();
        }

        public bool IsEmpty => Subcategories == null || Subcategories.All(sc => sc.IsEmpty);
        public int TotalItemCount =>
            (Subcategories != null && Subcategories.Count > 0)
                ? (Subcategories[0].Items?.Count ?? 0)
                : 0;
    }

    /// <summary>
    /// Scanner for world map objects with 4-level navigation.
    /// - Ctrl+PageUp/Down: Categories (Settlements, Quest Sites, Caravans, Biomes, Roads and rivers)
    /// - Shift+PageUp/Down: Subcategories (e.g., Player/Allied/Neutral/Hostile settlements, All/Roads/Rivers)
    /// - PageUp/Down: Item types within subcategory
    /// - Alt+PageUp/Down: Instances of same type (biome regions, road/river segments)
    /// </summary>
    public static class WorldScannerState
    {
        private static List<WorldScannerCategory> categories = new List<WorldScannerCategory>();
        private static int currentCategoryIndex = 0;
        private static int currentSubcategoryIndex = 0;
        private static int currentItemIndex = 0;
        private static int currentInstanceIndex = 0;
        private static bool autoJumpMode = false;

        // Cache for expensive biome/road/river calculations
        private static Dictionary<string, List<BiomeRegion>> cachedBiomeRegions = null;
        private static Dictionary<string, List<RoadSegment>> cachedRoadSegments = null;
        private static Dictionary<string, List<RoadSegment>> cachedRiverSegments = null;
        private static PlanetTile lastCacheOrigin = PlanetTile.Invalid;

        // Cache for full category list (count-based invalidation)
        private static List<WorldScannerCategory> cachedCategories = null;
        private static int lastSettlementCount = 0;
        private static int lastCaravanCount = 0;
        private static int lastWorldObjectCount = 0;
        private static int lastQuestCount = 0;
        private static int lastWaypointCount = 0;

        // Saved focus state for temporary category operations
        private static int savedCategoryIndex = -1;
        private static int savedSubcategoryIndex = -1;
        private static int savedItemIndex = -1;
        private static int savedInstanceIndex = -1;

        // Temporary category tracking
        private static WorldScannerCategory temporaryCategory = null;

        // Navigation session: within a Page Up/Down sequence the sort order and announce
        // distance cache are frozen. A session ends when the origin tile is written by
        // something other than our own auto-jump, when the category/subcategory changes,
        // when map state changes (cache miss), or when the scanner closes.
        private static bool navSessionActive = false;
        private static PlanetTile lastScannerOriginTile = PlanetTile.Invalid;
        private static bool scannerDrivenJumpInProgress = false;

        // Traversal-distance memo keyed by (origin tile id, target tile id). Flushed whenever
        // the navigation session is invalidated. Values are capped (see ANNOUNCE_TRAVERSAL_CAP).
        private const int ANNOUNCE_TRAVERSAL_CAP = 100;
        private static readonly Dictionary<long, float> traversalDistanceMemo = new Dictionary<long, float>();

        /// <summary>
        /// Toggles auto-jump mode on/off.
        /// </summary>
        public static void ToggleAutoJumpMode()
        {
            autoJumpMode = !autoJumpMode;
            string status = autoJumpMode ? "enabled" : "disabled";
            TolkHelper.Speak($"Auto-jump mode {status}", SpeechPriority.High);
        }

        /// <summary>
        /// Ends the current navigation session. The traversal-distance memo is flushed so
        /// subsequent announcements recompute against the new origin.
        /// </summary>
        public static void InvalidateNavigationSession()
        {
            navSessionActive = false;
            lastScannerOriginTile = PlanetTile.Invalid;
            traversalDistanceMemo.Clear();
        }

        /// <summary>
        /// Called by WorldNavigationState.CurrentSelectedTile setter on every origin write.
        /// Scanner-driven writes (JumpToCurrent) set scannerDrivenJumpInProgress, so external
        /// writes (arrow keys, bookmark jumps, settlement browser, quest notification, etc.)
        /// are what actually invalidate the session.
        /// </summary>
        public static void NotifyOriginWritten()
        {
            if (!scannerDrivenJumpInProgress)
                InvalidateNavigationSession();
        }

        /// <summary>
        /// Starts a fresh navigation session anchored to the current origin tile. Called
        /// implicitly on the first Page Up/Down of a sequence.
        /// </summary>
        private static void BeginNavigationSession()
        {
            navSessionActive = true;
            lastScannerOriginTile = WorldNavigationState.CurrentSelectedTile;
            traversalDistanceMemo.Clear();
        }

        /// <summary>
        /// Returns the traversal distance used for the announced distance. Capped at
        /// ANNOUNCE_TRAVERSAL_CAP (currently 100) so the pathfind terminates quickly even
        /// for far targets; memoized per (origin, target) within a session so rapid Page
        /// Up/Down over the same items does not repeatedly pathfind.
        ///
        /// `wasCapped` is true when the pathfind hit the cap, meaning the target is farther
        /// than the cap in traversal terms. Callers announce "over N tiles" in that case.
        /// Fuel and launch-range calculations are untouched by this cap — TransportPods and
        /// gravships do their own unbounded traversal against the target tile directly.
        /// </summary>
        private static float GetAnnouncementTraversalDistance(PlanetTile origin, PlanetTile target, out bool wasCapped)
        {
            wasCapped = false;
            if (!origin.Valid || !target.Valid)
                return 0f;

            long key = ((long)origin.tileId << 32) ^ (uint)target.tileId;
            if (traversalDistanceMemo.TryGetValue(key, out float cached))
            {
                wasCapped = cached >= ANNOUNCE_TRAVERSAL_CAP;
                return cached;
            }

            // maxDist is a float in RimWorld's API. Values >= ANNOUNCE_TRAVERSAL_CAP indicate
            // the pathfind hit the cap, which we treat as "over cap".
            float result = Find.WorldGrid.TraversalDistanceBetween(
                origin, target,
                passImpassable: true,
                maxDist: ANNOUNCE_TRAVERSAL_CAP,
                canTraverseLayers: true);

            // Some RimWorld paths return float.MaxValue on "unreachable within cap" — normalize
            // to the cap so the caller always gets a sane number to compare/announce.
            if (result >= ANNOUNCE_TRAVERSAL_CAP || float.IsInfinity(result) || float.IsNaN(result))
            {
                result = ANNOUNCE_TRAVERSAL_CAP;
                wasCapped = true;
            }

            traversalDistanceMemo[key] = result;
            return result;
        }

        /// <summary>
        /// Saves the current scanner focus state for later restoration.
        /// Used when temporarily switching to a search category.
        /// </summary>
        public static void SaveFocus()
        {
            savedCategoryIndex = currentCategoryIndex;
            savedSubcategoryIndex = currentSubcategoryIndex;
            savedItemIndex = currentItemIndex;
            savedInstanceIndex = currentInstanceIndex;
        }

        /// <summary>
        /// Restores the previously saved scanner focus state.
        /// Call this after removing a temporary category to return to the previous position.
        /// </summary>
        public static void RestoreFocus()
        {
            if (savedCategoryIndex >= 0)
            {
                currentCategoryIndex = savedCategoryIndex;
                currentSubcategoryIndex = savedSubcategoryIndex;
                currentItemIndex = savedItemIndex;
                currentInstanceIndex = savedInstanceIndex;

                // Reset saved state
                savedCategoryIndex = -1;
                savedSubcategoryIndex = -1;
                savedItemIndex = -1;
                savedInstanceIndex = -1;

                // Validate indices in case the category list changed
                ValidateIndices();
            }
        }

        /// <summary>
        /// Creates a temporary category with the given name and items, and selects it.
        /// Only one temporary category can exist at a time.
        /// </summary>
        /// <param name="name">The name for the temporary category</param>
        /// <param name="items">The items to include in the category</param>
        public static void CreateTemporaryCategory(string name, List<WorldScannerItem> items)
        {
            // Remove any existing temporary category first
            RemoveTemporaryCategory();

            // Create the new temporary category with a single subcategory
            temporaryCategory = new WorldScannerCategory(name);
            var subcategory = new WorldScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            temporaryCategory.Subcategories.Add(subcategory);

            // Add to the categories list
            categories.Add(temporaryCategory);

            // Select the temporary category
            currentCategoryIndex = categories.Count - 1;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentInstanceIndex = 0;

            // Search results are a different subcategory context — end any in-progress session.
            InvalidateNavigationSession();
        }

        /// <summary>
        /// Removes the temporary category if one exists.
        /// Call RestoreFocus() after this to return to the previous scanner position.
        /// </summary>
        public static void RemoveTemporaryCategory()
        {
            if (temporaryCategory != null)
            {
                categories.Remove(temporaryCategory);
                temporaryCategory = null;
                InvalidateNavigationSession();
            }
        }

        /// <summary>
        /// Checks if the scanner is currently focused on a temporary category.
        /// </summary>
        public static bool IsInTemporaryCategory()
        {
            return temporaryCategory != null && currentCategoryIndex >= 0 && currentCategoryIndex < categories.Count &&
                   categories[currentCategoryIndex] == temporaryCategory;
        }

        /// <summary>
        /// Refreshes the world scanner categories and items.
        /// </summary>
        private static void RefreshItems()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

            // Check if we can reuse cached categories
            int currentSettlements = Find.WorldObjects?.Settlements?.Count ?? 0;
            int currentCaravans = Find.WorldObjects?.Caravans?.Count ?? 0;
            int currentWorldObjects = Find.WorldObjects?.AllWorldObjects?.Count ?? 0;
            int currentQuests = Find.QuestManager?.questsInDisplayOrder?.Count ?? 0;
            int currentWaypoints = (Find.WorldRoutePlanner?.Active == true)
                ? (Find.WorldRoutePlanner.waypoints?.Count ?? 0) : 0;

            bool cacheValid = cachedCategories != null
                && cachedCategories.Count > 0
                && currentSettlements == lastSettlementCount
                && currentCaravans == lastCaravanCount
                && currentWorldObjects == lastWorldObjectCount
                && currentQuests == lastQuestCount
                && currentWaypoints == lastWaypointCount;

            if (cacheValid)
            {
                bool inLaunchMode = TransportPodLaunchState.IsActive || GravshipDestinationState.IsActive;
                categories = inLaunchMode
                    ? DeepCopyCategories(cachedCategories)
                    : new List<WorldScannerCategory>(cachedCategories);
                // The cached list does not include the active search / temporary category —
                // re-attach it so Page Up/Down still finds search results after a refresh.
                if (temporaryCategory != null && !categories.Contains(temporaryCategory))
                    categories.Add(temporaryCategory);
                FilterToReachableItems(originTile);
                ValidateIndices();
                return;
            }

            categories.Clear();

            // Category 0: Route Waypoints (only when route planner is active)
            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active && Find.WorldRoutePlanner.waypoints.Count > 0)
            {
                var waypointsCategory = CreateWaypointsCategory(originTile);
                if (!waypointsCategory.IsEmpty) categories.Add(waypointsCategory);
            }

            // Category 1: Settlements (with faction subcategories)
            var settlementsCategory = CreateSettlementsCategory(originTile);
            if (!settlementsCategory.IsEmpty) categories.Add(settlementsCategory);

            // Category 2: Quest Sites
            var questSitesCategory = CreateQuestSitesCategory(originTile);
            if (!questSitesCategory.IsEmpty) categories.Add(questSitesCategory);

            // Category 3: Caravans
            var caravansCategory = CreateCaravansCategory(originTile);
            if (!caravansCategory.IsEmpty) categories.Add(caravansCategory);

            // Category 4: Other Sites
            var otherSitesCategory = CreateOtherSitesCategory(originTile);
            if (!otherSitesCategory.IsEmpty) categories.Add(otherSitesCategory);

            // Category 5: Landmarks (Odyssey DLC)
            var landmarksCategory = CreateLandmarksCategory(originTile);
            if (!landmarksCategory.IsEmpty) categories.Add(landmarksCategory);

            // Category 6: Biomes (lazy-loaded when accessed)
            var biomesCategory = CreateBiomesCategory(originTile);
            if (!biomesCategory.IsEmpty) categories.Add(biomesCategory);

            // Category 6: Roads and rivers
            var roadsCategory = CreateRoadsAndRiversCategory(originTile);
            if (!roadsCategory.IsEmpty) categories.Add(roadsCategory);

            // Note: Space/orbital objects are now included in their proper categories above

            if (categories.Count == 0)
            {
                TolkHelper.Speak("No world objects found", SpeechPriority.High);
                return;
            }

            // Save cache (but not if a temporary search category is present)
            if (temporaryCategory == null)
            {
                cachedCategories = new List<WorldScannerCategory>(categories);
                lastSettlementCount = currentSettlements;
                lastCaravanCount = currentCaravans;
                lastWorldObjectCount = currentWorldObjects;
                lastQuestCount = currentQuests;
                lastWaypointCount = currentWaypoints;
            }

            // Re-attach the active search / temporary category after the rebuild. We cleared
            // `categories` at the top of this path, so the temp would otherwise be lost.
            if (temporaryCategory != null && !categories.Contains(temporaryCategory))
                categories.Add(temporaryCategory);

            // Map state changed (cache miss) — any in-progress navigation snapshot is stale.
            // Caravan dissolved, quest expired, trader arrived, etc.
            InvalidateNavigationSession();

            // Apply range filter for launch mode (after caching full data)
            // Deep-copy first so filtering doesn't corrupt the cache
            if (TransportPodLaunchState.IsActive || GravshipDestinationState.IsActive)
                categories = DeepCopyCategories(categories);
            FilterToReachableItems(originTile);

            ValidateIndices();
        }

        /// <summary>
        /// Filters scanner items to only show reachable destinations during launch targeting.
        /// Uses the same fuel calculations that power the fuel cost announcements.
        /// Called after building/loading categories — does not affect the cache.
        /// </summary>
        private static void FilterToReachableItems(PlanetTile originTile)
        {
            bool podLaunch = TransportPodLaunchState.IsActive;
            bool gravLaunch = GravshipDestinationState.IsActive;
            if (!podLaunch && !gravLaunch)
                return;

            for (int c = categories.Count - 1; c >= 0; c--)
            {
                var category = categories[c];
                for (int s = category.Subcategories.Count - 1; s >= 0; s--)
                {
                    var subcat = category.Subcategories[s];
                    subcat.Items.RemoveAll(item => !IsItemReachable(item, originTile, podLaunch));

                    // Trim out-of-range regions and update destination tiles to be reachable
                    foreach (var item in subcat.Items)
                    {
                        TrimOutOfRangeInstances(item, podLaunch);
                    }

                    if (subcat.IsEmpty)
                        category.Subcategories.RemoveAt(s);
                }

                if (category.IsEmpty)
                    categories.RemoveAt(c);
            }
        }

        /// <summary>
        /// Removes out-of-range instances (biome regions, road segments) from a multi-instance item.
        /// For regions where the center tile is out of range, finds a reachable tile from the
        /// region's actual tile set and replaces the center tile with it.
        /// </summary>
        private static void TrimOutOfRangeInstances(WorldScannerItem item, bool isPodLaunch)
        {
            if (item.BiomeRegions != null)
            {
                item.BiomeRegions.RemoveAll(r => !FindReachableTile(r.TileIds, isPodLaunch).Valid);
                foreach (var r in item.BiomeRegions)
                {
                    if (!IsTileReachable(r.CenterTile, isPodLaunch))
                        r.CenterTile = FindReachableTile(r.TileIds, isPodLaunch);
                }
                if (item.BiomeRegions.Count > 0)
                    item.Tile = item.BiomeRegions[0].CenterTile;
            }
            else if (item.RoadSegments != null)
            {
                item.RoadSegments.RemoveAll(r => !FindReachableTile(r.TileIds, isPodLaunch).Valid);
                foreach (var r in item.RoadSegments)
                {
                    if (!IsTileReachable(r.CenterTile, isPodLaunch))
                        r.CenterTile = FindReachableTile(r.TileIds, isPodLaunch);
                }
                if (item.RoadSegments.Count > 0)
                    item.Tile = item.RoadSegments[0].CenterTile;
            }
        }

        /// <summary>
        /// Finds the reachable tile in a region's tile set with the lowest traversal distance
        /// (i.e., cheapest fuel cost). Returns PlanetTile.Invalid if no tile is reachable.
        /// </summary>
        private static PlanetTile FindReachableTile(int[] tileIds, bool isPodLaunch)
        {
            if (tileIds == null) return PlanetTile.Invalid;

            PlanetTile bestTile = PlanetTile.Invalid;
            int bestDist = int.MaxValue;

            foreach (int tileId in tileIds)
            {
                int dist = isPodLaunch
                    ? TransportPodLaunchState.GetCachedDistance(new PlanetTile(tileId, -1))
                    : GravshipDestinationState.GetCachedDistance(new PlanetTile(tileId, -1));
                if (dist >= 0 && dist < bestDist)
                {
                    bestDist = dist;
                    bestTile = new PlanetTile(tileId, -1);
                }
            }
            return bestTile;
        }

        /// <summary>
        /// Checks if a specific tile is reachable during launch mode.
        /// </summary>
        private static bool IsTileReachable(PlanetTile tile, bool isPodLaunch)
        {
            if (!tile.Valid) return false;
            return isPodLaunch
                ? TransportPodLaunchState.CanReachTile(tile)
                : GravshipDestinationState.CanReachDestination(tile);
        }

        /// <summary>
        /// Creates a deep copy of categories so filtering doesn't corrupt the cache.
        /// Copies category, subcategory, and item list structures (items themselves are shared).
        /// </summary>
        private static List<WorldScannerCategory> DeepCopyCategories(List<WorldScannerCategory> source)
        {
            return source.Select(c =>
            {
                var copy = new WorldScannerCategory(c.Name);
                copy.Subcategories = c.Subcategories.Select(s =>
                {
                    var sCopy = new WorldScannerSubcategory(s.Name);
                    sCopy.Items = s.Items.Select(item =>
                    {
                        // Deep-copy biome/road items since filtering mutates CenterTile
                        if (item.BiomeRegions != null || item.RoadSegments != null)
                        {
                            var itemCopy = new WorldScannerItem(item.Label, item.Tile);
                            itemCopy.WorldObject = item.WorldObject;
                            itemCopy.Faction = item.Faction;
                            itemCopy.QuestName = item.QuestName;
                            if (item.BiomeRegions != null)
                                itemCopy.BiomeRegions = item.BiomeRegions.Select(r =>
                                    new BiomeRegion(r.CenterTile, r.TileCount)
                                    { TileIds = r.TileIds, Distance = r.Distance, SizeDescription = r.SizeDescription }
                                ).ToList();
                            if (item.RoadSegments != null)
                                itemCopy.RoadSegments = item.RoadSegments.Select(r =>
                                    new RoadSegment(r.CenterTile, r.TileCount)
                                    { TileIds = r.TileIds, Distance = r.Distance, Length = r.Length, SizeDescription = r.SizeDescription }
                                ).ToList();
                            return itemCopy;
                        }
                        return item; // Non-region items can be shared safely
                    }).ToList();
                    return sCopy;
                }).ToList();
                return copy;
            }).ToList();
        }

        /// <summary>
        /// Checks if a scanner item is reachable from the launch origin.
        /// For biome/road items, checks if ANY tile in ANY region is in the BFS reachable cache.
        /// For single-tile items (settlements, etc.), checks if that tile is reachable.
        /// </summary>
        private static bool IsItemReachable(WorldScannerItem item, PlanetTile originTile, bool isPodLaunch)
        {
            // Biome regions: check actual tile sets against BFS cache
            if (item.BiomeRegions != null)
            {
                foreach (var r in item.BiomeRegions)
                {
                    if (FindReachableTile(r.TileIds, isPodLaunch).Valid)
                        return true;
                }
                return false;
            }

            // Road segments: same pattern
            if (item.RoadSegments != null)
            {
                foreach (var r in item.RoadSegments)
                {
                    if (FindReachableTile(r.TileIds, isPodLaunch).Valid)
                        return true;
                }
                return false;
            }

            // Single-tile items (settlements, landmarks, quest sites, etc.)
            PlanetTile itemTile = item.GetTileAtInstance(0);
            return itemTile.Valid && IsTileReachable(itemTile, isPodLaunch);
        }

        /// <summary>
        /// Collects all world scanner items from all categories into a flat list.
        /// Used by ScannerSearchState for Z search to match against biomes, roads, settlements, etc.
        /// Deduplicates by underlying identity rather than item reference. Some categories share
        /// references between "All" and specialized subcategories (Settlements), which ref-dedup
        /// could handle — but Landmarks create distinct WorldScannerItem instances with different
        /// labels ("name, type" in All vs. just "name" in the type subcategory), so a ref-based
        /// dedup would miss those and every landmark would appear twice in search.
        /// </summary>
        public static List<WorldScannerItem> CollectAllItemsFlat()
        {
            // Use cached categories if available to avoid full rebuild per keystroke
            if (cachedCategories == null || cachedCategories.Count == 0)
                RefreshItems();

            var source = cachedCategories ?? categories;
            var allItems = new List<WorldScannerItem>();

            var seenWorldObjects = new HashSet<WorldObject>();
            var seenTiles = new HashSet<PlanetTile>();
            var seenBiomes = new HashSet<string>();
            var seenRoads = new HashSet<string>();
            var seenRefs = new HashSet<WorldScannerItem>();

            foreach (var category in source)
            {
                // Skip temporary search category to avoid including old search results
                if (category == temporaryCategory)
                    continue;

                foreach (var subcat in category.Subcategories)
                {
                    foreach (var item in subcat.Items)
                    {
                        if (IsDuplicateWorldItem(item, seenWorldObjects, seenTiles, seenBiomes, seenRoads, seenRefs))
                            continue;
                        allItems.Add(item);
                    }
                }
            }
            return allItems;
        }

        /// <summary>
        /// Returns true when this item's underlying identity has already been seen.
        /// Identity precedence: WorldObject &gt; biome label &gt; road label &gt; tile &gt; item ref.
        /// Landmark items have no WorldObject and are keyed by Tile, which correctly matches
        /// a landmark's "name, type" All-subcategory copy against its "name"-only type-subcategory copy.
        /// </summary>
        private static bool IsDuplicateWorldItem(
            WorldScannerItem item,
            HashSet<WorldObject> seenWorldObjects,
            HashSet<PlanetTile> seenTiles,
            HashSet<string> seenBiomes,
            HashSet<string> seenRoads,
            HashSet<WorldScannerItem> seenRefs)
        {
            if (item.WorldObject != null)
                return !seenWorldObjects.Add(item.WorldObject);
            if (item.BiomeRegions != null)
                return !seenBiomes.Add(item.Label ?? "");
            if (item.RoadSegments != null)
                return !seenRoads.Add(item.Label ?? "");
            if (item.Tile.Valid)
                return !seenTiles.Add(item.Tile);
            return !seenRefs.Add(item);
        }

        #region Category Creators

        private static WorldScannerCategory CreateWaypointsCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Route Waypoints");
            var subcat = new WorldScannerSubcategory("Waypoints");

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
            {
                category.Subcategories.Add(subcat);
                return category;
            }

            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                RoutePlannerWaypoint waypoint = planner.waypoints[i];
                if (waypoint == null || !waypoint.Tile.Valid)
                    continue;

                var item = new WorldScannerItem(waypoint);

                StringBuilder label = new StringBuilder();
                label.Append($"Waypoint {i + 1}");

                string tileName = WorldInfoHelper.GetTileSummary(waypoint.Tile, includeRouteInfo: false, minimal: true);
                if (!string.IsNullOrEmpty(tileName))
                {
                    label.Append($": {tileName}");
                }

                if (i >= 1)
                {
                    int ticksToWaypoint = planner.GetTicksToWaypoint(i);
                    string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
                    label.Append($". Estimated travel time: {timeString}");
                }
                else
                {
                    label.Append(" (Start)");
                }

                item.Label = label.ToString();
                subcat.Items.Add(item);
            }

            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateSettlementsCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Settlements");

            var allSubcat = new WorldScannerSubcategory("All");
            var playerSubcat = new WorldScannerSubcategory("Player");
            var alliedSubcat = new WorldScannerSubcategory("Allied");
            var neutralSubcat = new WorldScannerSubcategory("Neutral");
            var hostileSubcat = new WorldScannerSubcategory("Hostile");

            var settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (var settlement in settlements)
                {
                    if (settlement.Faction == null || !settlement.Tile.Valid)
                        continue;

                    allSubcat.Items.Add(new WorldScannerItem(settlement));
                }
            }

            // Sort once, then split into faction subcategories preserving sort order
            SortItemsByDistance(allSubcat.Items, originTile);

            foreach (var item in allSubcat.Items)
            {
                var settlement = item.WorldObject as Settlement;
                if (settlement == null) continue;

                if (settlement.Faction == Faction.OfPlayer)
                {
                    playerSubcat.Items.Add(item);
                }
                else
                {
                    var relation = settlement.Faction.RelationKindWith(Faction.OfPlayer);
                    switch (relation)
                    {
                        case FactionRelationKind.Ally:
                            alliedSubcat.Items.Add(item);
                            break;
                        case FactionRelationKind.Neutral:
                            neutralSubcat.Items.Add(item);
                            break;
                        case FactionRelationKind.Hostile:
                            hostileSubcat.Items.Add(item);
                            break;
                    }
                }
            }

            category.Subcategories.Add(allSubcat);
            category.Subcategories.Add(playerSubcat);
            category.Subcategories.Add(alliedSubcat);
            category.Subcategories.Add(neutralSubcat);
            category.Subcategories.Add(hostileSubcat);

            return category;
        }

        private static WorldScannerCategory CreateQuestSitesCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Quest Sites");
            var subcat = new WorldScannerSubcategory("Active Quests");

            if (Find.QuestManager != null)
            {
                foreach (Quest quest in Find.QuestManager.questsInDisplayOrder)
                {
                    if (quest.State != QuestState.Ongoing || quest.hidden || quest.hiddenInUI) continue;
                    foreach (GlobalTargetInfo target in quest.QuestLookTargets)
                    {
                        if (!target.IsValid || !target.IsWorldTarget)
                            continue;

                        WorldObject worldObj = null;
                        PlanetTile tile = PlanetTile.Invalid;

                        if (target.HasWorldObject && target.WorldObject != null)
                        {
                            worldObj = target.WorldObject;
                            tile = worldObj.Tile;
                        }
                        else if (target.Tile.Valid)
                        {
                            tile = target.Tile;
                            worldObj = Find.WorldObjects?.ObjectsAt(tile)?.FirstOrDefault();
                        }

                        if (!tile.Valid || worldObj == null) continue;

                        // Skip player settlements
                        if (worldObj is Settlement settlement && settlement.Faction == Faction.OfPlayer)
                            continue;

                        var item = new WorldScannerItem(worldObj);
                        item.QuestName = quest.name.StripTags();
                        subcat.Items.Add(item);
                    }
                }
            }

            SortItemsByDistance(subcat.Items, originTile);
            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateCaravansCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Caravans");
            var subcat = new WorldScannerSubcategory("Player Caravans");

            var caravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();

            if (caravans != null)
            {
                foreach (var caravan in caravans)
                {
                    var item = new WorldScannerItem(caravan);
                    subcat.Items.Add(item);
                }
            }

            SortItemsByDistance(subcat.Items, originTile);
            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateOtherSitesCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Other Sites");
            var subcat = new WorldScannerSubcategory("Sites");

            var allObjects = Find.WorldObjects?.AllWorldObjects;
            if (allObjects != null)
            {
                var questTiles = new HashSet<int>();
                if (Find.QuestManager != null)
                {
                    foreach (var quest in Find.QuestManager.questsInDisplayOrder)
                    {
                        if (quest.State != QuestState.Ongoing) continue;
                        foreach (var target in quest.QuestLookTargets)
                        {
                            if (target.IsValid && target.IsWorldTarget)
                            {
                                if (target.HasWorldObject)
                                    questTiles.Add(target.WorldObject.Tile);
                                else if (target.Tile.Valid)
                                    questTiles.Add(target.Tile);
                            }
                        }
                    }
                }

                foreach (var worldObj in allObjects)
                {
                    if (worldObj is Settlement || worldObj is Caravan)
                        continue;
                    if (questTiles.Contains(worldObj.Tile))
                        continue;
                    if (!worldObj.Tile.Valid)
                        continue;

                    var item = new WorldScannerItem(worldObj);
                    subcat.Items.Add(item);
                }
            }

            SortItemsByDistance(subcat.Items, originTile);
            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateBiomesCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Biomes");
            var subcat = new WorldScannerSubcategory("All Biomes");

            // Check if we need to rebuild the cache
            if (cachedBiomeRegions == null || !lastCacheOrigin.Valid ||
                Find.WorldGrid.ApproxDistanceInTiles(lastCacheOrigin, originTile) > 50)
            {
                cachedBiomeRegions = CollectBiomeRegions(originTile);
                lastCacheOrigin = originTile;
            }

            // Create items for each biome type, sorted by closest region
            var biomeItems = new List<WorldScannerItem>();
            foreach (var kvp in cachedBiomeRegions)
            {
                string biomeName = kvp.Key;
                var regions = kvp.Value;

                if (regions.Count == 0) continue;

                // Update distances for all regions
                foreach (var region in regions)
                {
                    region.Distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, region.CenterTile);
                }

                // Sort regions by distance
                regions.Sort((a, b) => a.Distance.CompareTo(b.Distance));

                var item = new WorldScannerItem(biomeName, regions);
                biomeItems.Add(item);
            }

            // Sort biome types by their closest region
            SortItemsByDistance(biomeItems, originTile);

            subcat.Items.AddRange(biomeItems);
            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateRoadsAndRiversCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Roads and rivers");

            // Check if we need to rebuild the caches (lastCacheOrigin is set by biome collection)
            bool cacheStale = !lastCacheOrigin.Valid ||
                Find.WorldGrid.ApproxDistanceInTiles(lastCacheOrigin, originTile) > 50;
            if (cachedRoadSegments == null || cacheStale)
                cachedRoadSegments = CollectRoadSegments(originTile);
            if (cachedRiverSegments == null || cacheStale)
                cachedRiverSegments = CollectRiverSegments(originTile);

            var roadItems = BuildSegmentItems(cachedRoadSegments, originTile);
            var riverItems = BuildSegmentItems(cachedRiverSegments, originTile);

            // When both kinds are present, lead with a combined "All" list;
            // with only one kind "All" would just duplicate it. "Roads" and
            // "Rivers" split them out. Each subcategory is added only if it
            // has items.
            if (roadItems.Count > 0 && riverItems.Count > 0)
            {
                var allItems = new List<WorldScannerItem>(roadItems.Count + riverItems.Count);
                allItems.AddRange(roadItems);
                allItems.AddRange(riverItems);
                SortItemsByDistance(allItems, originTile);
                AddSubcategoryIfAny(category, "All", allItems);
            }

            AddSubcategoryIfAny(category, "Roads", roadItems);
            AddSubcategoryIfAny(category, "Rivers", riverItems);

            return category;
        }

        private static void AddSubcategoryIfAny(
            WorldScannerCategory category, string name, List<WorldScannerItem> items)
        {
            if (items == null || items.Count == 0) return;
            var subcat = new WorldScannerSubcategory(name);
            subcat.Items.AddRange(items);
            category.Subcategories.Add(subcat);
        }

        /// <summary>
        /// Builds scanner items (one per feature type) from collected segments,
        /// updating per-segment distances and sorting both segments and items by
        /// distance from the origin. Shared by roads and rivers.
        /// </summary>
        private static List<WorldScannerItem> BuildSegmentItems(
            Dictionary<string, List<RoadSegment>> segmentsByType, PlanetTile originTile)
        {
            var items = new List<WorldScannerItem>();
            if (segmentsByType == null) return items;

            foreach (var kvp in segmentsByType)
            {
                string name = kvp.Key;
                var segments = kvp.Value;

                if (segments.Count == 0) continue;

                // Update distances
                foreach (var segment in segments)
                {
                    segment.Distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, segment.CenterTile);
                }

                segments.Sort((a, b) => a.Distance.CompareTo(b.Distance));

                items.Add(new WorldScannerItem(name, segments));
            }

            SortItemsByDistance(items, originTile);
            return items;
        }

        private static WorldScannerCategory CreateLandmarksCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("Landmarks");

            if (!ModsConfig.OdysseyActive || Find.World?.landmarks == null)
                return category;

            var landmarks = Find.World.landmarks.landmarks;
            if (landmarks == null || landmarks.Count == 0)
                return category;

            var allSubcat = new WorldScannerSubcategory("All");

            // Group landmarks by their def label for type subcategories
            var typeGroups = new Dictionary<string, WorldScannerSubcategory>();

            foreach (var kvp in landmarks)
            {
                PlanetTile tile = kvp.Key;
                Landmark landmark = kvp.Value;
                if (landmark?.def == null || !tile.Valid)
                    continue;

                string name = landmark.name ?? landmark.def.LabelCap;
                string defLabel = landmark.def.LabelCap;

                // Label includes both name and type for announcements
                string label = $"{name}, {defLabel}";

                var item = new WorldScannerItem(label, tile);
                allSubcat.Items.Add(item);

                // Add to type-specific subcategory (label is just the name since type is in the subcategory name)
                if (!typeGroups.TryGetValue(defLabel, out var typeSubcat))
                {
                    typeSubcat = new WorldScannerSubcategory(defLabel);
                    typeGroups[defLabel] = typeSubcat;
                }
                // Use name-only label for type-specific subcategories to avoid redundancy
                typeSubcat.Items.Add(new WorldScannerItem(name, tile));
            }

            SortItemsByDistance(allSubcat.Items, originTile);
            category.Subcategories.Add(allSubcat);

            // Add type subcategories sorted alphabetically
            foreach (var typeSubcat in typeGroups.Values.OrderBy(s => s.Name))
            {
                SortItemsByDistance(typeSubcat.Items, originTile);
                category.Subcategories.Add(typeSubcat);
            }

            return category;
        }

        /// <summary>
        /// Checks if a world object is on the surface layer (not in space/orbit).
        /// </summary>
        private static bool IsOnSurfaceLayer(WorldObject worldObject)
        {
            if (worldObject == null || !worldObject.Tile.Valid)
                return false;

            // Check if the tile's layer is the surface layer
            return worldObject.Tile.Layer == Find.WorldGrid.Surface;
        }

        /// <summary>
        /// Checks if an item's tile is on a different planet layer than the currently selected one.
        /// Used for cross-layer distance calculation and layer annotations.
        /// </summary>
        private static bool IsOnDifferentLayer(WorldScannerItem item)
        {
            if (item.WorldObject == null || !item.WorldObject.Tile.Valid)
                return false;
            return item.WorldObject.Tile.Layer != PlanetLayer.Selected;
        }

        #endregion

        #region Biome and Road Collection

        private static Dictionary<string, List<BiomeRegion>> CollectBiomeRegions(PlanetTile originTile)
        {
            var result = new Dictionary<string, List<BiomeRegion>>();

            if (Find.WorldGrid == null || Find.World == null)
                return result;

            // Get tiles within a reasonable range (performance optimization)
            int maxRange = 200; // tiles
            var visited = new HashSet<int>();
            var tilesByBiome = new Dictionary<string, HashSet<int>>();

            // BFS from origin to collect nearby tiles by biome
            var queue = new Queue<int>();
            queue.Enqueue(originTile);
            visited.Add(originTile);

            while (queue.Count > 0 && visited.Count < 125000) // Limit total tiles
            {
                int currentTileId = queue.Dequeue();
                PlanetTile currentTile = new PlanetTile(currentTileId, -1);

                if (!currentTile.Valid) continue;

                float dist = Find.WorldGrid.ApproxDistanceInTiles(originTile, currentTile);
                if (dist > maxRange) continue;

                Tile tileData = currentTile.Tile;
                if (tileData?.PrimaryBiome != null)
                {
                    string biomeName = tileData.PrimaryBiome.LabelCap;
                    if (!tilesByBiome.ContainsKey(biomeName))
                        tilesByBiome[biomeName] = new HashSet<int>();
                    tilesByBiome[biomeName].Add(currentTileId);
                }

                // Add neighbors
                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // For each biome, find contiguous regions using flood fill
            foreach (var kvp in tilesByBiome)
            {
                string biomeName = kvp.Key;
                var biomeTiles = new HashSet<int>(kvp.Value);
                var regions = new List<BiomeRegion>();

                while (biomeTiles.Count > 0)
                {
                    int startTile = 0;
                    foreach (int t in biomeTiles) { startTile = t; break; }
                    var regionTiles = FloodFillRegion(startTile, biomeTiles);

                    if (regionTiles.Count > 0)
                    {
                        // Find center tile (closest to centroid)
                        PlanetTile centerTile = FindRegionCenter(regionTiles);
                        // Use the constructor that calculates span
                        var region = new BiomeRegion(centerTile, regionTiles);
                        regions.Add(region);
                    }

                    // Remove processed tiles
                    foreach (int tile in regionTiles)
                        biomeTiles.Remove(tile);
                }

                if (regions.Count > 0)
                    result[biomeName] = regions;
            }

            return result;
        }

        private static HashSet<int> FloodFillRegion(int startTile, HashSet<int> validTiles)
        {
            var region = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startTile);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!validTiles.Contains(current) || region.Contains(current))
                    continue;

                region.Add(current);

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(new PlanetTile(current, -1), neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (validTiles.Contains(neighbor) && !region.Contains(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return region;
        }

        private static PlanetTile FindRegionCenter(HashSet<int> regionTiles)
        {
            if (regionTiles.Count == 0)
                return PlanetTile.Invalid;

            // Calculate centroid
            Vector3 centroid = Vector3.zero;
            foreach (int tileId in regionTiles)
            {
                centroid += Find.WorldGrid.GetTileCenter(new PlanetTile(tileId, -1));
            }
            centroid /= regionTiles.Count;

            // Find tile closest to centroid
            int closestTile = 0;
            foreach (int t in regionTiles) { closestTile = t; break; }
            float closestDist = float.MaxValue;

            foreach (int tileId in regionTiles)
            {
                Vector3 tilePos = Find.WorldGrid.GetTileCenter(new PlanetTile(tileId, -1));
                float dist = Vector3.Distance(tilePos, centroid);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestTile = tileId;
                }
            }

            return new PlanetTile(closestTile, -1);
        }

        private static Dictionary<string, List<RoadSegment>> CollectRoadSegments(PlanetTile originTile)
        {
            var result = new Dictionary<string, List<RoadSegment>>();

            if (Find.WorldGrid == null)
                return result;

            int maxRange = 200;
            var visited = new HashSet<int>();
            var roadTilesByType = new Dictionary<string, HashSet<int>>();

            var queue = new Queue<int>();
            queue.Enqueue(originTile);
            visited.Add(originTile);

            while (queue.Count > 0 && visited.Count < 125000)
            {
                int currentTileId = queue.Dequeue();
                PlanetTile currentTile = new PlanetTile(currentTileId, -1);

                if (!currentTile.Valid) continue;

                float dist = Find.WorldGrid.ApproxDistanceInTiles(originTile, currentTile);
                if (dist > maxRange) continue;

                Tile tileData = currentTile.Tile;
                if (tileData is SurfaceTile surfaceTile && surfaceTile.Roads != null)
                {
                    foreach (var roadLink in surfaceTile.Roads)
                    {
                        string roadName = roadLink.road.LabelCap;
                        if (!roadTilesByType.ContainsKey(roadName))
                            roadTilesByType[roadName] = new HashSet<int>();
                        roadTilesByType[roadName].Add(currentTileId);
                    }
                }

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // For roads, we treat each connected section as a "segment"
            foreach (var kvp in roadTilesByType)
            {
                string roadName = kvp.Key;
                var roadTiles = new HashSet<int>(kvp.Value);
                var segments = new List<RoadSegment>();

                while (roadTiles.Count > 0)
                {
                    int startTile = 0;
                    foreach (int t in roadTiles) { startTile = t; break; }
                    var segmentTiles = FloodFillRoadSegment(startTile, roadTiles, roadName);

                    if (segmentTiles.Count > 0)
                    {
                        PlanetTile centerTile = FindRegionCenter(segmentTiles);
                        // Use the constructor that calculates length
                        var segment = new RoadSegment(centerTile, segmentTiles);
                        segments.Add(segment);
                    }

                    foreach (int tile in segmentTiles)
                        roadTiles.Remove(tile);
                }

                if (segments.Count > 0)
                    result[roadName] = segments;
            }

            return result;
        }

        private static HashSet<int> FloodFillRoadSegment(int startTile, HashSet<int> validTiles, string roadType)
        {
            var segment = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startTile);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!validTiles.Contains(current) || segment.Contains(current))
                    continue;

                segment.Add(current);

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(new PlanetTile(current, -1), neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (validTiles.Contains(neighbor) && !segment.Contains(neighbor))
                    {
                        // Check if neighbor has the same road type
                        Tile neighborData = neighbor.Tile;
                        if (neighborData is SurfaceTile surfaceTile && surfaceTile.Roads != null)
                        {
                            bool hasRoadType = false;
                            foreach (var road in surfaceTile.Roads)
                            {
                                if (road.road.LabelCap == roadType)
                                {
                                    hasRoadType = true;
                                    break;
                                }
                            }
                            if (hasRoadType)
                                queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return segment;
        }

        private static Dictionary<string, List<RoadSegment>> CollectRiverSegments(PlanetTile originTile)
        {
            var result = new Dictionary<string, List<RoadSegment>>();

            if (Find.WorldGrid == null)
                return result;

            int maxRange = 200;
            var visited = new HashSet<int>();
            var riverTilesByType = new Dictionary<string, HashSet<int>>();

            var queue = new Queue<int>();
            queue.Enqueue(originTile);
            visited.Add(originTile);

            while (queue.Count > 0 && visited.Count < 125000)
            {
                int currentTileId = queue.Dequeue();
                PlanetTile currentTile = new PlanetTile(currentTileId, -1);

                if (!currentTile.Valid) continue;

                float dist = Find.WorldGrid.ApproxDistanceInTiles(originTile, currentTile);
                if (dist > maxRange) continue;

                Tile tileData = currentTile.Tile;
                if (tileData is SurfaceTile surfaceTile && surfaceTile.Rivers != null)
                {
                    foreach (var riverLink in surfaceTile.Rivers)
                    {
                        string riverName = riverLink.river.LabelCap;
                        if (!riverTilesByType.ContainsKey(riverName))
                            riverTilesByType[riverName] = new HashSet<int>();
                        riverTilesByType[riverName].Add(currentTileId);
                    }
                }

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(currentTile, neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Treat each connected stretch of a river type as a "segment"
            foreach (var kvp in riverTilesByType)
            {
                string riverName = kvp.Key;
                var riverTiles = new HashSet<int>(kvp.Value);
                var segments = new List<RoadSegment>();

                while (riverTiles.Count > 0)
                {
                    int startTile = 0;
                    foreach (int t in riverTiles) { startTile = t; break; }
                    var segmentTiles = FloodFillRiverSegment(startTile, riverTiles, riverName);

                    if (segmentTiles.Count > 0)
                    {
                        PlanetTile centerTile = FindRegionCenter(segmentTiles);
                        var segment = new RoadSegment(centerTile, segmentTiles);
                        segments.Add(segment);
                    }

                    foreach (int tile in segmentTiles)
                        riverTiles.Remove(tile);
                }

                if (segments.Count > 0)
                    result[riverName] = segments;
            }

            return result;
        }

        private static HashSet<int> FloodFillRiverSegment(int startTile, HashSet<int> validTiles, string riverType)
        {
            var segment = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startTile);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!validTiles.Contains(current) || segment.Contains(current))
                    continue;

                segment.Add(current);

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(new PlanetTile(current, -1), neighbors);
                foreach (var neighbor in neighbors)
                {
                    if (validTiles.Contains(neighbor) && !segment.Contains(neighbor))
                    {
                        // Check if neighbor carries the same river type
                        Tile neighborData = neighbor.Tile;
                        if (neighborData is SurfaceTile surfaceTile && surfaceTile.Rivers != null)
                        {
                            bool hasRiverType = false;
                            foreach (var river in surfaceTile.Rivers)
                            {
                                if (river.river.LabelCap == riverType)
                                {
                                    hasRiverType = true;
                                    break;
                                }
                            }
                            if (hasRiverType)
                                queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return segment;
        }

        #endregion

        #region Navigation

        private static void SortItemsByDistance(List<WorldScannerItem> items, PlanetTile originTile)
        {
            items.Sort((a, b) => a.GetDistance(originTile, 0).CompareTo(b.GetDistance(originTile, 0)));
        }

        /// <summary>
        /// Recalculates distances and re-sorts only the current subcategory's items.
        /// Call after changing indices to ensure the viewed subcategory is sorted
        /// by distance from the user's current position.
        /// </summary>
        private static void RecalculateCurrentDistances()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;
            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            SortItemsByDistance(subcat.Items, originTile);
        }

        /// <summary>
        /// Invalidates all scanner caches. Call when world state changes externally
        /// or when the scanner is closed/reset.
        /// </summary>
        public static void InvalidateCache()
        {
            cachedCategories = null;
            lastSettlementCount = 0;
            lastCaravanCount = 0;
            lastWorldObjectCount = 0;
            lastQuestCount = 0;
            lastWaypointCount = 0;
            cachedBiomeRegions = null;
            cachedRoadSegments = null;
            cachedRiverSegments = null;
            lastCacheOrigin = PlanetTile.Invalid;
        }

        private static void ValidateIndices()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                currentCategoryIndex = 0;

            var category = GetCurrentCategory();
            if (category != null)
            {
                if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                    currentSubcategoryIndex = 0;
                SkipEmptySubcategories(forward: true);
            }

            var subcat = GetCurrentSubcategory();
            if (subcat != null)
            {
                if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                    currentItemIndex = 0;
            }

            var item = GetCurrentItem();
            if (item != null)
            {
                if (currentInstanceIndex < 0 || currentInstanceIndex >= item.InstanceCount)
                    currentInstanceIndex = 0;
            }
        }

        private static void SkipEmptySubcategories(bool forward)
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            int attempts = 0;
            int maxAttempts = category.Subcategories.Count;

            while ((GetCurrentSubcategory()?.IsEmpty ?? true) && attempts < maxAttempts)
            {
                if (forward)
                {
                    currentSubcategoryIndex++;
                    if (currentSubcategoryIndex >= category.Subcategories.Count)
                        currentSubcategoryIndex = 0;
                }
                else
                {
                    currentSubcategoryIndex--;
                    if (currentSubcategoryIndex < 0)
                        currentSubcategoryIndex = category.Subcategories.Count - 1;
                }
                attempts++;
            }
        }

        private static WorldScannerCategory GetCurrentCategory()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                return null;
            return categories[currentCategoryIndex];
        }

        private static WorldScannerSubcategory GetCurrentSubcategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return null;
            if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                return null;
            return category.Subcategories[currentSubcategoryIndex];
        }

        private static WorldScannerItem GetCurrentItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return null;
            if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                return null;
            return subcat.Items[currentItemIndex];
        }

        /// <summary>
        /// Moves to the next category (Ctrl+PageDown).
        /// </summary>
        public static void NextCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            // Changing category ends the current navigation session.
            InvalidateNavigationSession();

            currentCategoryIndex++;
            if (currentCategoryIndex >= categories.Count)
                currentCategoryIndex = 0;

            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentInstanceIndex = 0;
            SkipEmptySubcategories(forward: true);
            RecalculateCurrentDistances();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves to the previous category (Ctrl+PageUp).
        /// </summary>
        public static void PreviousCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            // Changing category ends the current navigation session.
            InvalidateNavigationSession();

            currentCategoryIndex--;
            if (currentCategoryIndex < 0)
                currentCategoryIndex = categories.Count - 1;

            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentInstanceIndex = 0;
            SkipEmptySubcategories(forward: true);
            RecalculateCurrentDistances();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves to the next subcategory (Shift+PageDown).
        /// </summary>
        public static void NextSubcategory()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var category = GetCurrentCategory();
            if (category == null || category.Subcategories.Count <= 1)
            {
                TolkHelper.Speak("No subcategories", SpeechPriority.Normal);
                return;
            }

            // Changing subcategory ends the current navigation session.
            InvalidateNavigationSession();

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex++;
                if (currentSubcategoryIndex >= category.Subcategories.Count)
                    currentSubcategoryIndex = 0;
                if (currentSubcategoryIndex == startIndex) break;
            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            currentItemIndex = 0;
            currentInstanceIndex = 0;
            RecalculateCurrentDistances();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves to the previous subcategory (Shift+PageUp).
        /// </summary>
        public static void PreviousSubcategory()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var category = GetCurrentCategory();
            if (category == null || category.Subcategories.Count <= 1)
            {
                TolkHelper.Speak("No subcategories", SpeechPriority.Normal);
                return;
            }

            // Changing subcategory ends the current navigation session.
            InvalidateNavigationSession();

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex--;
                if (currentSubcategoryIndex < 0)
                    currentSubcategoryIndex = category.Subcategories.Count - 1;
                if (currentSubcategoryIndex == startIndex) break;
            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            currentItemIndex = 0;
            currentInstanceIndex = 0;
            RecalculateCurrentDistances();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves to the next item type (PageDown).
        /// </summary>
        public static void NextItem()
        {
            if (!WorldNavigationState.IsActive) return;

            // Always refresh: catches world object changes (caravan dissolves, quest expires,
            // new trader arrives, etc.). RefreshItems is cheap on a cache hit and invalidates
            // the nav session on a miss so the next press re-sorts against the fresh data.
            bool wasEmpty = categories.Count == 0;
            RefreshItems();
            if (categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                return;
            }

            AdvanceInSession(forward: true, subcat);
        }

        /// <summary>
        /// Moves to the previous item type (PageUp).
        /// </summary>
        public static void PreviousItem()
        {
            if (!WorldNavigationState.IsActive) return;

            bool wasEmpty = categories.Count == 0;
            RefreshItems();
            if (categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                return;
            }

            AdvanceInSession(forward: false, subcat);
        }

        /// <summary>
        /// Shared navigation step for NextItem / PreviousItem. Starts a session on the first
        /// press and advances the index through the frozen sort order on subsequent presses.
        /// Has a drift safety net: if the origin tile is not where the last scanner jump left
        /// it, invalidates before proceeding so a fresh session begins.
        /// </summary>
        private static void AdvanceInSession(bool forward, WorldScannerSubcategory subcat)
        {
            if (navSessionActive
                && lastScannerOriginTile.Valid
                && WorldNavigationState.CurrentSelectedTile != lastScannerOriginTile)
            {
                InvalidateNavigationSession();
            }

            if (!navSessionActive)
            {
                BeginNavigationSession();
            }

            if (forward)
            {
                currentItemIndex++;
                if (currentItemIndex >= subcat.Items.Count)
                    currentItemIndex = 0;
            }
            else
            {
                currentItemIndex--;
                if (currentItemIndex < 0)
                    currentItemIndex = subcat.Items.Count - 1;
            }

            currentInstanceIndex = 0;

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentItem();

            lastScannerOriginTile = WorldNavigationState.CurrentSelectedTile;
        }

        /// <summary>
        /// Moves to the next instance of the current item type (Alt+PageDown).
        /// </summary>
        public static void NextInstance()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null || !item.HasInstances)
            {
                TolkHelper.Speak("No instances to navigate", SpeechPriority.Normal);
                return;
            }

            currentInstanceIndex++;
            if (currentInstanceIndex >= item.InstanceCount)
                currentInstanceIndex = 0;

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentInstance();
        }

        /// <summary>
        /// Moves to the previous instance of the current item type (Alt+PageUp).
        /// </summary>
        public static void PreviousInstance()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null || !item.HasInstances)
            {
                TolkHelper.Speak("No instances to navigate", SpeechPriority.Normal);
                return;
            }

            currentInstanceIndex--;
            if (currentInstanceIndex < 0)
                currentInstanceIndex = item.InstanceCount - 1;

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentInstance();
        }

        /// <summary>
        /// Jumps the camera to the current item/instance (Home key).
        /// </summary>
        public static void JumpToCurrent()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            PlanetTile targetTile = item.GetTileAtInstance(currentInstanceIndex);

            // Auto-switch layer if jumping to an item on a different layer
            if (targetTile.Valid && targetTile.Layer != PlanetLayer.Selected)
            {
                PlanetLayer.Selected = targetTile.Layer;
                TolkHelper.Speak($"Switched to {targetTile.LayerDef.LabelCap} layer.");
            }

            // Guard the origin write so NotifyOriginWritten does not invalidate the in-progress
            // navigation session — this is a scanner-driven move, not an external cursor change.
            scannerDrivenJumpInProgress = true;
            try
            {
                WorldNavigationState.CurrentSelectedTile = targetTile;
            }
            finally
            {
                scannerDrivenJumpInProgress = false;
            }

            // Sync with game selection (WorldSelector + WorldInterface/GameInitData for world gen)
            WorldNavigationState.SyncSelectionWithGame();
            // Also select the world object if applicable
            if (item.WorldObject != null && Find.WorldSelector != null)
                Find.WorldSelector.Select(item.WorldObject);

            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(targetTile);
                Find.WorldCameraDriver.RotateSoNorthIsUp();
            }

            // Announce full tile info as if the user arrowed to this tile,
            // which also updates BiomeDescriptionTracker for the new location
            WorldNavigationState.AnnounceTile();
        }

        /// <summary>
        /// Reads distance and direction to current item (End key).
        /// </summary>
        public static void ReadDistanceAndDirection()
        {
            if (!WorldNavigationState.IsActive) return;

            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            float distance = item.GetDistance(originTile, currentInstanceIndex);
            string direction = item.GetDirectionFrom(originTile, currentInstanceIndex);

            TolkHelper.Speak($"{direction}, {distance:F0} tiles", SpeechPriority.Normal);
        }

        #endregion

        #region Announcements

        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            int catPos = currentCategoryIndex + 1;
            int catTotal = categories.Count;

            TolkHelper.Speak($"{category.Name}, {category.TotalItemCount} items. Category {catPos} of {catTotal}", SpeechPriority.Normal);
        }

        private static void AnnounceCurrentSubcategory()
        {
            var subcat = GetCurrentSubcategory();
            var category = GetCurrentCategory();
            if (subcat == null || category == null) return;

            int subPos = currentSubcategoryIndex + 1;
            int subTotal = category.Subcategories.Count;

            TolkHelper.Speak($"{subcat.Name}, {subcat.Items.Count} items. Subcategory {subPos} of {subTotal}", SpeechPriority.Normal);
        }

        private static void AnnounceCurrentItem()
        {
            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                return;
            }

            var subcat = GetCurrentSubcategory();
            var category = GetCurrentCategory();

            // Check if item is on a different planet layer (e.g., orbital object while on surface)
            bool onDifferentLayer = IsOnDifferentLayer(item);

            var parts = new List<string>();

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            float distance = 0f;
            string direction = "";
            bool distanceCapped = false;

            if (onDifferentLayer)
            {
                // Cross-layer: project origin to target layer for comparable distance
                PlanetTile itemTile = item.GetTileAtInstance(0);
                if (itemTile.Valid && originTile.Valid)
                {
                    PlanetTile projected = itemTile.Layer.GetClosestTile_NewTemp(originTile);
                    if (projected.Valid)
                    {
                        distance = Find.WorldGrid.ApproxDistanceInTiles(projected, itemTile);
                        direction = item.GetDirectionFrom(projected, 0);
                    }
                }
            }
            else
            {
                direction = item.GetDirectionFrom(originTile, 0);

                // Use a capped + memoized traversal distance for the announcement so rapid
                // Page Up/Down stays responsive on big worlds. Fuel / launch range are
                // validated separately below against the target tile directly, so the cap
                // does not affect range or fuel correctness.
                PlanetTile targetTile = item.GetTileAtInstance(0);
                if (originTile.Valid && targetTile.Valid)
                    distance = GetAnnouncementTraversalDistance(originTile, targetTile, out distanceCapped);

                // Check reachability from last waypoint if route planner has waypoints
                if (targetTile.Valid && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active &&
                    Find.WorldRoutePlanner.waypoints.Count > 0 && Find.WorldReachability != null)
                {
                    var lastWaypoint = Find.WorldRoutePlanner.waypoints[Find.WorldRoutePlanner.waypoints.Count - 1];
                    if (lastWaypoint != null && lastWaypoint.Tile.Valid)
                    {
                        if (!Find.WorldReachability.CanReach(lastWaypoint.Tile, targetTile))
                        {
                            parts.Add("Unreachable");
                        }
                    }
                }
            }

            parts.Add(item.Label);

            if (!string.IsNullOrEmpty(item.QuestName))
                parts.Add($"Quest: {item.QuestName}");

            // For settlements, add faction type and goodwill (matching WorldInfoHelper format)
            if (item.WorldObject is Settlement settlement && item.Faction != null && item.Faction != Faction.OfPlayer)
            {
                // Faction name and type (e.g., "The Tribe, Tribal")
                string factionType = item.Faction.def?.LabelCap ?? "";
                if (!string.IsNullOrEmpty(factionType) && factionType != item.Faction.Name)
                    parts.Add($"{item.Faction.Name}, {factionType}");
                else
                    parts.Add(item.Faction.Name);

                // Relationship with goodwill (e.g., "Neutral +15")
                string relationship = item.Faction.HostileTo(Faction.OfPlayer) ? "Hostile" :
                                     item.Faction.PlayerRelationKind.GetLabelCap();
                int goodwill = item.Faction.PlayerGoodwill;
                string goodwillStr = goodwill >= 0 ? $"+{goodwill}" : goodwill.ToString();
                parts.Add($"{relationship} {goodwillStr}");

            }

            // For biomes/roads, show instance count
            if (item.HasInstances)
            {
                if (item.BiomeRegions != null)
                    parts.Add($"{item.BiomeRegions[0].SizeDescription}");
                else if (item.RoadSegments != null)
                    parts.Add($"{item.RoadSegments[0].SizeDescription}");

                parts.Add($"Region 1 of {item.InstanceCount}");
            }

            if (!string.IsNullOrEmpty(direction) && distance > 0.1f)
            {
                if (distanceCapped)
                    parts.Add($"{direction}, over {ANNOUNCE_TRAVERSAL_CAP:F0} tiles");
                else
                    parts.Add($"{direction}, {distance:F0} tiles");
            }
            else if (distance <= 0.1f && !onDifferentLayer)
            {
                parts.Add("Current location");
            }

            // Append layer name for cross-layer items
            if (onDifferentLayer)
            {
                var itemTile = item.GetTileAtInstance(0);
                if (itemTile.Valid)
                {
                    string layerName = itemTile.LayerDef.LabelCap;
                    parts.Add($"on {layerName} layer");
                }
            }

            // Add fuel cost if transport pod or gravship launch targeting is active
            if (TransportPodLaunchState.ShouldAnnounceFuelCosts() && distance > 0.1f)
            {
                // Use tile-based method for accurate traversal distance (matches game's range check)
                PlanetTile itemTile = item.GetTileAtInstance(0);
                string fuelInfo = itemTile.Valid
                    ? TransportPodLaunchState.GetFuelCostAnnouncementForTile(itemTile)
                    : TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }
            else if (GravshipDestinationState.ShouldAnnounceFuelCosts())
            {
                PlanetTile itemTile = item.GetTileAtInstance(0);
                if (itemTile.Valid)
                {
                    string fuelInfo = GravshipDestinationState.GetFuelCostAnnouncement(itemTile);
                    if (!string.IsNullOrEmpty(fuelInfo))
                        parts.Add(fuelInfo);
                }
            }

            int pos = currentItemIndex + 1;
            int total = subcat?.Items.Count ?? 0;
            parts.Add($"{pos} of {total}");

            TolkHelper.Speak(string.Join(". ", parts), SpeechPriority.Normal);
        }

        private static void AnnounceCurrentInstance()
        {
            var item = GetCurrentItem();
            if (item == null || !item.HasInstances) return;

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            string direction = item.GetDirectionFrom(originTile, currentInstanceIndex);

            // Capped + memoized traversal distance for responsive instance navigation.
            PlanetTile targetTile = item.GetTileAtInstance(currentInstanceIndex);
            bool distanceCapped = false;
            float distance = (originTile.Valid && targetTile.Valid)
                ? GetAnnouncementTraversalDistance(originTile, targetTile, out distanceCapped)
                : 0f;

            var parts = new List<string>();

            // Check reachability from last waypoint if route planner has waypoints
            if (targetTile.Valid && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active &&
                Find.WorldRoutePlanner.waypoints.Count > 0 && Find.WorldReachability != null)
            {
                var lastWaypoint = Find.WorldRoutePlanner.waypoints[Find.WorldRoutePlanner.waypoints.Count - 1];
                if (lastWaypoint != null && lastWaypoint.Tile.Valid)
                {
                    if (!Find.WorldReachability.CanReach(lastWaypoint.Tile, targetTile))
                    {
                        parts.Add("Unreachable");
                    }
                }
            }

            parts.Add(item.Label);

            if (item.BiomeRegions != null && currentInstanceIndex < item.BiomeRegions.Count)
            {
                var region = item.BiomeRegions[currentInstanceIndex];
                parts.Add(region.SizeDescription);
            }
            else if (item.RoadSegments != null && currentInstanceIndex < item.RoadSegments.Count)
            {
                var segment = item.RoadSegments[currentInstanceIndex];
                parts.Add(segment.SizeDescription);
            }

            if (!string.IsNullOrEmpty(direction) && distance > 0.1f)
            {
                if (distanceCapped)
                    parts.Add($"{direction}, over {ANNOUNCE_TRAVERSAL_CAP:F0} tiles");
                else
                    parts.Add($"{direction}, {distance:F0} tiles");
            }
            else if (distance <= 0.1f)
                parts.Add("Current location");

            // Add fuel cost if transport pod or gravship launch targeting is active
            if (TransportPodLaunchState.ShouldAnnounceFuelCosts() && distance > 0.1f)
            {
                string fuelInfo = TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }
            else if (GravshipDestinationState.ShouldAnnounceFuelCosts() && targetTile.Valid)
            {
                string fuelInfo = GravshipDestinationState.GetFuelCostAnnouncement(targetTile);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }

            int pos = currentInstanceIndex + 1;
            int total = item.InstanceCount;
            parts.Add($"Region {pos} of {total}");

            TolkHelper.Speak(string.Join(". ", parts), SpeechPriority.Normal);
        }

        #endregion

        /// <summary>
        /// Resets the scanner state.
        /// </summary>
        public static void Reset()
        {
            categories.Clear();
            currentCategoryIndex = 0;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentInstanceIndex = 0;
            InvalidateCache();
            temporaryCategory = null;
            savedCategoryIndex = -1;
            savedSubcategoryIndex = -1;
            savedItemIndex = -1;
            savedInstanceIndex = -1;

            // End any in-progress navigation session.
            InvalidateNavigationSession();

            // Clear any active search
            ScannerSearchState.ClearSearchSilent();
        }
    }
}
