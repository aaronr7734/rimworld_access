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
    /// Scanner for world map objects, navigated on four levels: Ctrl+PageUp/Down for categories,
    /// Shift+PageUp/Down for subcategories, PageUp/Down for item types, and Alt+PageUp/Down for
    /// instances of one type (biome regions, road and river segments).
    /// </summary>
    public static partial class WorldScannerState
    {
        // Shared index-cursor/session skeleton (src/Scanner/ScannerCursor.cs) — see that file's
        // doc comments for what it owns vs. what stays here. ExtraIndex is the region/segment
        // instance index. Temporary-category tracking lives on worldCursor too.
        private static readonly ScannerCursor<WorldScannerCategory, WorldScannerSubcategory, WorldScannerItem> worldCursor =
            new ScannerCursor<WorldScannerCategory, WorldScannerSubcategory, WorldScannerItem>();

        private static bool autoJumpMode
        {
            get
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings != null && settings.ScannerAutoJump;
            }
        }

        private static Dictionary<string, List<BiomeRegion>> cachedBiomeRegions = null;
        private static Dictionary<string, List<RoadSegment>> cachedRoadSegments = null;
        private static Dictionary<string, List<RoadSegment>> cachedRiverSegments = null;
        private static PlanetTile lastCacheOrigin = PlanetTile.Invalid;

        // Full category list, invalidated by count.
        private static List<WorldScannerCategory> cachedCategories = null;
        private static int lastSettlementCount = 0;
        private static int lastCaravanCount = 0;
        private static int lastWorldObjectCount = 0;
        private static int lastQuestCount = 0;
        private static int lastWaypointCount = 0;

        // Navigation session: within a Page Up/Down sequence the sort order and announce
        // distance cache are frozen. A session ends when the origin tile is written by
        // something other than our own auto-jump, when the category/subcategory changes,
        // when map state changes (cache miss), or when the scanner closes.
        private static readonly ScannerNavSession<PlanetTile> navSession = new ScannerNavSession<PlanetTile>();

        // Traversal-distance memo keyed by (origin tile id, target tile id). Flushed whenever
        // the navigation session is invalidated. Values are capped (see ANNOUNCE_TRAVERSAL_CAP).
        private const int ANNOUNCE_TRAVERSAL_CAP = 100;
        private static readonly Dictionary<long, float> traversalDistanceMemo = new Dictionary<long, float>();

        // Cheap geometric metric used by the shared closest-tile logic to pick a region's nearest
        // member tile to the cursor. The announced *traversal* distance still uses the capped /
        // memoized pathfind; this only ranks candidate member tiles.
        private static readonly Func<int, int, float> WorldTileMetric =
            (a, b) => Find.WorldGrid.ApproxDistanceInTiles(new PlanetTile(a, -1), new PlanetTile(b, -1));

        // Guards against the two world keyboard routes both delivering a manual Home in one frame:
        // a double fire now reads as nearest-then-center.
        private static int lastManualJumpFrame = -1;

        private static bool InLaunchMode =>
            TransportPodLaunchState.IsActive || GravshipDestinationState.IsActive;

        /// <summary>
        /// The member tiles the closest-tile feature may target for a region instance. Outside
        /// launch mode that is every tile of the region. During launch targeting it is only the
        /// tiles actually reachable from the launch origin, so Home and the announced distance /
        /// fuel never point at a tile you cannot launch to. Returns an empty array when no member
        /// is reachable (callers then fall back to the reachable center tile FilterToReachableItems
        /// guarantees), and null for single-tile items (settlements, caravans, landmarks).
        /// </summary>
        private static int[] FeatureMemberTiles(WorldScannerItem item, int instanceIndex)
        {
            int[] members = item.GetMemberTileIdsAtInstance(instanceIndex);
            if (members == null || members.Length == 0 || !InLaunchMode)
                return members;

            bool podLaunch = TransportPodLaunchState.IsActive;
            return members.Where(t => IsTileReachable(new PlanetTile(t, -1), podLaunch)).ToArray();
        }

        /// <summary>
        /// The instance (region/segment) whose nearest tile is closest to <paramref name="origin"/>
        /// — the world equivalent of the local scanner's FindNearestRegionIndex. This is the region
        /// the item-level announcement describes, so standing on a large patch reports that patch
        /// (distance 0) rather than a distant region whose center merely sits closer. Uses the
        /// reachable tiles during a launch.
        /// </summary>
        private static int NearestInstanceIndex(WorldScannerItem item, PlanetTile origin)
        {
            if (item == null || !item.HasInstances) return 0;

            int best = 0;
            float bestDist = float.MaxValue;
            int count = item.InstanceCount;
            for (int i = 0; i < count; i++)
            {
                float d = WorldScannerItem.NearestTileDistance(FeatureMemberTiles(item, i), origin);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public static void ToggleAutoJumpMode()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            settings.ScannerAutoJump = !settings.ScannerAutoJump;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            TolkHelper.Speak(settings.ScannerAutoJump
                ? "RimWorldAccess.WorldScanner.AutoJumpEnabled".Loc()
                : "RimWorldAccess.WorldScanner.AutoJumpDisabled".Loc(), SpeechPriority.High);
        }

        /// <summary>Ends the navigation session, flushing the traversal memo so later announcements recompute against the new origin.</summary>
        public static void InvalidateNavigationSession()
        {
            navSession.Invalidate();
            traversalDistanceMemo.Clear();
        }

        /// <summary>
        /// Called on every origin write. Scanner-driven writes flag themselves, so only external
        /// writes (arrow keys, bookmark jumps, the settlement browser) invalidate the session.
        /// </summary>
        public static void NotifyOriginWritten()
        {
            if (!navSession.ScannerDrivenJumpInProgress)
                InvalidateNavigationSession();
        }

        /// <summary>Starts a session anchored to the current origin tile, implicitly on the first Page Up/Down of a sequence.</summary>
        private static void BeginNavigationSession()
        {
            navSession.Begin(WorldNavigationState.CurrentSelectedTile);
            traversalDistanceMemo.Clear();
        }

        /// <summary>
        /// The traversal distance behind the announced distance, capped at ANNOUNCE_TRAVERSAL_CAP so
        /// the pathfind terminates quickly, and memoized per (origin, target) within a session.
        /// <paramref name="wasCapped"/> means the target is farther than the cap, which callers
        /// announce as "over N tiles". Fuel and launch-range calculations do their own unbounded
        /// traversal and are untouched by the cap.
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

            // A result at or above the cap means the pathfind hit it.
            float result = Find.WorldGrid.TraversalDistanceBetween(
                origin, target,
                passImpassable: true,
                maxDist: ANNOUNCE_TRAVERSAL_CAP,
                canTraverseLayers: true);

            // Some paths return float.MaxValue for "unreachable within cap"; normalize to the cap so
            // callers always get a comparable number.
            if (result >= ANNOUNCE_TRAVERSAL_CAP || float.IsInfinity(result) || float.IsNaN(result))
            {
                result = ANNOUNCE_TRAVERSAL_CAP;
                wasCapped = true;
            }

            traversalDistanceMemo[key] = result;
            return result;
        }

        /// <summary>Saves the scanner focus state, for the temporary switch into a search category.</summary>
        public static void SaveFocus()
        {
            worldCursor.SaveFocus();
        }

        /// <summary>Restores the saved focus state; call after removing a temporary category.</summary>
        public static void RestoreFocus()
        {
            worldCursor.RestoreFocus(item => item.InstanceCount);
        }

        /// <summary>Creates and selects a temporary category; only one can exist at a time.</summary>
        public static void CreateTemporaryCategory(string name, List<WorldScannerItem> items)
        {
            RemoveTemporaryCategory();

            var category = new WorldScannerCategory(name);
            var subcategory = new WorldScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            category.Subcategories.Add(subcategory);

            worldCursor.SelectTemporaryCategory(category);

            // Search results are a different subcategory context.
            InvalidateNavigationSession();
        }

        /// <summary>Removes the temporary category; call <see cref="RestoreFocus"/> afterwards.</summary>
        public static void RemoveTemporaryCategory()
        {
            if (worldCursor.RemoveTemporaryCategory())
            {
                InvalidateNavigationSession();
            }
        }

        public static bool IsInTemporaryCategory()
        {
            return worldCursor.IsInTemporaryCategory();
        }

        /// <summary>
        /// World scanner category builders in display order; each takes the origin tile and returns
        /// a possibly-empty category, which RefreshItems drops when empty. Adding or reordering a
        /// category is a one-line change here.
        /// </summary>
        private static readonly List<Func<PlanetTile, WorldScannerCategory>> CategoryBuilders =
            new List<Func<PlanetTile, WorldScannerCategory>>
            {
                CreateWaypointsCategory,
                CreateSettlementsCategory,
                CreateQuestSitesCategory,
                CreateCaravansCategory,
                CreateOutpostsCategory,
                CreateOtherSitesCategory,
                CreateLandmarksCategory,
                CreateBiomesCategory,
                CreateRoadsAndRiversCategory,
            };

        private static void RefreshItems()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NavNotActive".Loc(), SpeechPriority.High);
                return;
            }

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

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
                worldCursor.Categories = inLaunchMode
                    ? DeepCopyCategories(cachedCategories)
                    : new List<WorldScannerCategory>(cachedCategories);
                // The cache holds no temporary category, so re-attach it or Page Up/Down loses the
                // search results after a refresh.
                worldCursor.ReattachTemporaryCategoryIfMissing();
                FilterToReachableItems(originTile);
                ValidateIndices();
                return;
            }

            worldCursor.Categories.Clear();

            // Empty categories are dropped so they never appear in the Ctrl+PageUp/Down cycle.
            foreach (var build in CategoryBuilders)
            {
                var category = build(originTile);
                if (category != null && !category.IsEmpty)
                    worldCursor.Categories.Add(category);
            }

            if (worldCursor.Categories.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoWorldObjects".Loc(), SpeechPriority.High);
                return;
            }

            // Never cache a list carrying a temporary search category.
            if (worldCursor.TemporaryCategory == null)
            {
                cachedCategories = new List<WorldScannerCategory>(worldCursor.Categories);
                lastSettlementCount = currentSettlements;
                lastCaravanCount = currentCaravans;
                lastWorldObjectCount = currentWorldObjects;
                lastQuestCount = currentQuests;
                lastWaypointCount = currentWaypoints;
            }

            // Re-attach the temporary category: this path cleared Categories at the top.
            worldCursor.ReattachTemporaryCategoryIfMissing();

            // Cache miss means map state changed, so any in-progress navigation snapshot is stale.
            InvalidateNavigationSession();

            // Launch-mode range filter, applied after caching; deep-copied so it cannot corrupt it.
            if (TransportPodLaunchState.IsActive || GravshipDestinationState.IsActive)
                worldCursor.Categories = DeepCopyCategories(worldCursor.Categories);
            FilterToReachableItems(originTile);

            ValidateIndices();
        }

        /// <summary>
        /// Narrows items to reachable destinations during launch targeting, through the same fuel
        /// calculations the fuel-cost announcements use. Runs after the cache and never touches it.
        /// </summary>
        private static void FilterToReachableItems(PlanetTile originTile)
        {
            bool podLaunch = TransportPodLaunchState.IsActive;
            bool gravLaunch = GravshipDestinationState.IsActive;
            if (!podLaunch && !gravLaunch)
                return;

            for (int c = worldCursor.Categories.Count - 1; c >= 0; c--)
            {
                var category = worldCursor.Categories[c];
                for (int s = category.Subcategories.Count - 1; s >= 0; s--)
                {
                    var subcat = category.Subcategories[s];
                    subcat.Items.RemoveAll(item => !IsItemReachable(item, originTile, podLaunch));

                    foreach (var item in subcat.Items)
                    {
                        TrimOutOfRangeInstances(item, podLaunch);
                    }

                    if (subcat.IsEmpty)
                        category.Subcategories.RemoveAt(s);
                }

                if (category.IsEmpty)
                    worldCursor.Categories.RemoveAt(c);
            }
        }

        /// <summary>
        /// Drops out-of-range instances from a multi-instance item, replacing a region's
        /// out-of-range center tile with a reachable tile from its own tile set.
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

        /// <summary>The reachable tile of lowest traversal distance in a region, or PlanetTile.Invalid when none is.</summary>
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

        private static bool IsTileReachable(PlanetTile tile, bool isPodLaunch)
        {
            if (!tile.Valid) return false;
            return isPodLaunch
                ? TransportPodLaunchState.CanReachTile(tile)
                : GravshipDestinationState.CanReachDestination(tile);
        }

        /// <summary>Deep-copies the category/subcategory/item-list structure so filtering cannot corrupt the cache; the items themselves are shared.</summary>
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
                        // Biome and road items are copied too: filtering mutates CenterTile.
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
        /// Whether an item is reachable from the launch origin: for biome and road items, whether
        /// ANY tile of ANY region is in the BFS reachable cache; otherwise, whether its tile is.
        /// </summary>
        private static bool IsItemReachable(WorldScannerItem item, PlanetTile originTile, bool isPodLaunch)
        {
            if (item.BiomeRegions != null)
            {
                foreach (var r in item.BiomeRegions)
                {
                    if (FindReachableTile(r.TileIds, isPodLaunch).Valid)
                        return true;
                }
                return false;
            }

            if (item.RoadSegments != null)
            {
                foreach (var r in item.RoadSegments)
                {
                    if (FindReachableTile(r.TileIds, isPodLaunch).Valid)
                        return true;
                }
                return false;
            }

            PlanetTile itemTile = item.GetTileAtInstance(0);
            return itemTile.Valid && IsTileReachable(itemTile, isPodLaunch);
        }

        /// <summary>
        /// Every scanner item, flattened, for the search state. Deduplicated by underlying identity
        /// rather than by reference: Landmarks create distinct WorldScannerItem instances with
        /// different labels per subcategory, so a reference dedup would list each landmark twice.
        /// </summary>
        public static List<WorldScannerItem> CollectAllItemsFlat()
        {
            // Reuse the cache when possible: this runs per keystroke.
            if (cachedCategories == null || cachedCategories.Count == 0)
                RefreshItems();

            var source = cachedCategories ?? worldCursor.Categories;
            var allItems = new List<WorldScannerItem>();

            var seenWorldObjects = new HashSet<WorldObject>();
            var seenTiles = new HashSet<PlanetTile>();
            var seenBiomes = new HashSet<string>();
            var seenRoads = new HashSet<string>();
            var seenRefs = new HashSet<WorldScannerItem>();

            foreach (var category in source)
            {
                // Skip the temporary category, whose items are the previous search's results.
                if (category == worldCursor.TemporaryCategory)
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
        /// Whether this item's identity has been seen, by precedence: WorldObject, biome label, road
        /// label, tile, then item reference. Landmarks have no WorldObject and key by Tile, which
        /// matches their differently-labelled copies across subcategories.
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

    }
}
