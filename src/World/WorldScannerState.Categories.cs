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
    public static partial class WorldScannerState
    {
        #region Category Creators

        private static WorldScannerCategory CreateWaypointsCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.RouteWaypoints".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Waypoints".Translate());

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
                label.Append("RimWorldAccess.WorldScanner.Waypoint".Translate(i + 1).ToString());

                string tileName = WorldInfoHelper.GetTileSummary(waypoint.Tile, includeRouteInfo: false, minimal: true);
                if (!string.IsNullOrEmpty(tileName))
                {
                    label.Append(": ").Append(tileName);
                }

                if (i >= 1)
                {
                    int ticksToWaypoint = planner.GetTicksToWaypoint(i);
                    string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
                    label.Append(". ").Append("RimWorldAccess.WorldScanner.EstimatedTravelTime".Translate(timeString).ToString());
                }
                else
                {
                    label.Append(" ").Append("RimWorldAccess.WorldScanner.WaypointStart".Translate().ToString());
                }

                item.Label = label.ToString();
                subcat.Items.Add(item);
            }

            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateSettlementsCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.Settlements".Translate());

            var allSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.All".Translate());
            var playerSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Player".Translate());
            var alliedSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Allied".Translate());
            var neutralSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Neutral".Translate());
            var hostileSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Hostile".Translate());

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
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.QuestSites".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.ActiveQuests".Translate());

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
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.Caravans".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.PlayerCaravans".Translate());

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

        private static bool IsPlayerOutpost(WorldObject worldObj)
        {
            return worldObj is MapParent && !(worldObj is Settlement) && worldObj.Faction == Faction.OfPlayer;
        }

        private static WorldScannerCategory CreateOutpostsCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.Outposts".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.PlayerOutposts".Translate());

            var allObjects = Find.WorldObjects?.AllWorldObjects;
            if (allObjects != null)
            {
                foreach (var worldObj in allObjects)
                {
                    if (!IsPlayerOutpost(worldObj) || !worldObj.Tile.Valid)
                        continue;

                    var item = new WorldScannerItem(worldObj);
                    string kind = worldObj.def?.label;
                    if (!string.IsNullOrEmpty(kind) && !string.Equals(kind, item.Label, StringComparison.OrdinalIgnoreCase))
                        item.Label = "RimWorldAccess.WorldScanner.NamedOutpost".Translate(item.Label, kind);
                    subcat.Items.Add(item);
                }
            }

            SortItemsByDistance(subcat.Items, originTile);
            category.Subcategories.Add(subcat);
            return category;
        }

        private static WorldScannerCategory CreateOtherSitesCategory(PlanetTile originTile)
        {
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.OtherSites".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.Sites".Translate());

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
                    if (worldObj is Settlement || worldObj is Caravan || IsPlayerOutpost(worldObj))
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
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.Biomes".Translate());
            var subcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.AllBiomes".Translate());

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
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.RoadsAndRivers".Translate());

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
                AddSubcategoryIfAny(category, "RimWorldAccess.WorldScanner.Sub.All".Translate(), allItems);
            }

            AddSubcategoryIfAny(category, "RimWorldAccess.WorldScanner.Sub.Roads".Translate(), roadItems);
            AddSubcategoryIfAny(category, "RimWorldAccess.WorldScanner.Sub.Rivers".Translate(), riverItems);

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
            var category = new WorldScannerCategory("RimWorldAccess.WorldScanner.Cat.Landmarks".Translate());

            if (!ModsConfig.OdysseyActive || Find.World?.landmarks == null)
                return category;

            var landmarks = Find.World.landmarks.landmarks;
            if (landmarks == null || landmarks.Count == 0)
                return category;

            var allSubcat = new WorldScannerSubcategory("RimWorldAccess.WorldScanner.Sub.All".Translate());

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
                    var regionTiles = Clump.Fill(startTile, biomeTiles, WorldTileNeighbors);

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

        /// <summary>
        /// Yields the planet-grid neighbors of a tile as int tile ids. The shared
        /// <see cref="Clump"/> flood-fill gates each neighbor on the valid-tile set, so this only
        /// enumerates the grid adjacency. Shared by biome, road, and river collection — the
        /// road/river "same type" constraint is encoded by the valid-tile set (built from exactly
        /// the tiles carrying that road/river), not by an extra per-neighbor check.
        /// </summary>
        private static IEnumerable<int> WorldTileNeighbors(int tileId)
        {
            if (Find.WorldGrid == null)
                yield break;
            var neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(tileId, -1), neighbors);
            foreach (var neighbor in neighbors)
                yield return neighbor.tileId;
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
                    var segmentTiles = Clump.Fill(startTile, roadTiles, WorldTileNeighbors);

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
                    var segmentTiles = Clump.Fill(startTile, riverTiles, WorldTileNeighbors);

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

        #endregion

    }
}
