using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents a contiguous region of terrain tiles (e.g., a patch of rich soil).
    /// Used for adjacency-based grouping in the scanner.
    /// </summary>
    public class TerrainRegion
    {
        public IntVec3 CenterPosition { get; set; }
        public int TileCount { get; set; }
        public string Dimensions { get; set; } // "4x3" for rectangular shapes, null otherwise
        public List<IntVec3> AllPositions { get; set; }
        public float Distance { get; set; }
        public int? TotalQuantity { get; set; }  // For deep ore deposits

        /// <summary>
        /// Gets a human-readable size description ("4x3" or "12 tiles").
        /// </summary>
        public string SizeDescription => Dimensions ?? (string)"RimWorldAccess.Map.Scanner.Region.SizeTiles".Translate(TileCount);

        public TerrainRegion(List<IntVec3> positions, IntVec3 cursorPosition)
        {
            AllPositions = positions;
            TileCount = positions.Count;
            CenterPosition = CalculateCenter(positions);
            Dimensions = CalculateDimensions(positions);
            Distance = (CenterPosition - cursorPosition).LengthHorizontal;
        }

        /// <summary>
        /// Constructor for deep ore regions that tracks quantity per cell.
        /// </summary>
        public TerrainRegion(List<(IntVec3 position, int count)> positionsWithCounts, IntVec3 cursorPosition)
        {
            AllPositions = positionsWithCounts.Select(p => p.position).ToList();
            TileCount = AllPositions.Count;
            TotalQuantity = positionsWithCounts.Sum(p => p.count);
            CenterPosition = CalculateCenter(AllPositions);
            Dimensions = CalculateDimensions(AllPositions);
            Distance = (CenterPosition - cursorPosition).LengthHorizontal;
        }

        /// <summary>
        /// Calculates the center of a region, preferring a position that's actually in the region.
        /// </summary>
        private static IntVec3 CalculateCenter(List<IntVec3> positions)
        {
            if (positions.Count == 0)
                return IntVec3.Invalid;

            // Calculate centroid with proper rounding (not truncation)
            int sumX = 0, sumZ = 0;
            foreach (var pos in positions)
            {
                sumX += pos.x;
                sumZ += pos.z;
            }
            // Use Math.Round to avoid systematic bias from integer truncation
            int avgX = (int)Math.Round((double)sumX / positions.Count);
            int avgZ = (int)Math.Round((double)sumZ / positions.Count);
            var centroid = new IntVec3(avgX, 0, avgZ);

            // If centroid is in region, use it
            if (positions.Contains(centroid))
                return centroid;

            // Otherwise find the closest position to the centroid
            IntVec3 closest = positions[0];
            float closestDist = float.MaxValue;
            foreach (var pos in positions)
            {
                float dist = (pos - centroid).LengthHorizontal;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = pos;
                }
            }
            return closest;
        }

        /// <summary>
        /// Calculates dimensions if the region is rectangular ("4x3"), otherwise returns null.
        /// </summary>
        private static string CalculateDimensions(List<IntVec3> positions)
        {
            if (positions.Count == 0)
                return null;

            // Calculate bounding box
            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;

            foreach (var pos in positions)
            {
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.z < minZ) minZ = pos.z;
                if (pos.z > maxZ) maxZ = pos.z;
            }

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            // If tile count equals area, it's rectangular
            if (positions.Count == width * height)
            {
                // Return dimensions with larger dimension first for consistency
                if (width >= height)
                    return $"{width}x{height}";
                else
                    return $"{height}x{width}";
            }

            return null; // Irregular shape
        }
    }

    public class ScannerItem
    {
        public Thing Thing { get; set; }
        public List<Thing> BulkThings { get; set; } // For grouped items of the same type
        public List<IntVec3> BulkTerrainPositions { get; set; } // For grouped terrain tiles
        public Designation Designation { get; set; } // For designation items
        public List<Designation> BulkDesignations { get; set; } // For grouped designations of the same type
        public List<TerrainRegion> TerrainRegions { get; set; } // For adjacency-grouped terrain regions
        public float Distance { get; set; }
        public string Label { get; set; }
        public IntVec3 Position { get; set; }
        public bool IsTerrain { get; set; } // True if this represents terrain instead of a Thing
        public bool IsDesignation => Designation != null; // True if this represents a designation
        public Zone Zone { get; set; } // For zone items
        public bool IsZone => Zone != null; // True if this represents a zone
        public Room Room { get; set; } // For room items
        public bool IsRoom => Room != null; // True if this represents a room

        // Holding platform reference for captured Anomaly entities. When set, Thing is the
        // held pawn (not spawned on the map) and Position is the platform's position so
        // navigation/jump behavior works.
        public Building_HoldingPlatform HoldingPlatform { get; set; }
        public bool IsCapturedEntity => HoldingPlatform != null;
        public bool HasTerrainRegions => TerrainRegions != null && TerrainRegions.Count > 0;
        public int RegionCount => TerrainRegions?.Count ?? 0;
        public int TotalTileCount => TerrainRegions?.Sum(r => r.TileCount) ?? BulkTerrainPositions?.Count ?? 1;
        public int BulkCount => BulkThings?.Count ?? (BulkTerrainPositions?.Count ?? (BulkDesignations?.Count ?? (TerrainRegions?.Count ?? 1)));
        public bool IsBulkGroup => (BulkThings != null && BulkThings.Count > 1) ||
                                   (BulkTerrainPositions != null && BulkTerrainPositions.Count > 1) ||
                                   (BulkDesignations != null && BulkDesignations.Count > 1) ||
                                   (TerrainRegions != null && TerrainRegions.Count > 1);

        // Deep ore deposit properties
        public ThingDef DeepOreDef { get; set; }
        public int TotalQuantityAcrossRegions => TerrainRegions?
            .Where(r => r.TotalQuantity.HasValue)
            .Sum(r => r.TotalQuantity.Value) ?? 0;
        public bool HasQuantityInfo => TerrainRegions?.Any(r => r.TotalQuantity.HasValue) ?? false;

        // Set to true by RefreshLabel when the underlying Thing has been destroyed or despawned.
        // Callers should check this after RefreshLabel() and skip/remove stale items.
        public bool IsStale { get; private set; }

        // Live view over BulkThings that filters out destroyed/despawned entries.
        // Returns an empty enumerable when BulkThings is null.
        public IEnumerable<Thing> LiveBulkThings =>
            BulkThings?.Where(t => t != null && !t.Destroyed && t.Spawned) ?? Enumerable.Empty<Thing>();

        public ScannerItem(Thing thing, IntVec3 cursorPosition)
        {
            Thing = thing;
            Position = thing.Position;
            Distance = (thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(thing);
        }

        // Constructor for bulk groups
        public ScannerItem(List<Thing> things, IntVec3 cursorPosition)
        {
            if (things == null || things.Count == 0)
                throw new ArgumentException("Bulk group must contain at least one thing");

            BulkThings = things;
            Thing = things[0]; // Primary thing (closest)
            Position = Thing.Position;
            Distance = (Thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(Thing);
        }

        // Constructor for terrain tiles (no actual Thing object)
        public ScannerItem(IntVec3 cell, string label, IntVec3 cursorPosition)
        {
            Thing = null;
            Position = cell;
            Distance = (cell - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for grouped terrain tiles (legacy - non-adjacent grouping)
        public ScannerItem(List<IntVec3> positions, string label, IntVec3 cursorPosition)
        {
            if (positions == null || positions.Count == 0)
                throw new ArgumentException("Terrain group must contain at least one position");

            Thing = null;
            BulkTerrainPositions = positions;
            Position = positions[0]; // Primary position (closest)
            Distance = (positions[0] - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for adjacency-grouped terrain regions (e.g., separate patches of rich soil)
        public ScannerItem(List<TerrainRegion> regions, string label, IntVec3 cursorPosition)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Terrain regions list must contain at least one region");

            Thing = null;
            TerrainRegions = regions;
            // Position is the center of the closest region
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for adjacency-grouped mineable regions (ore/rock with Thing reference)
        public ScannerItem(List<TerrainRegion> regions, string label, IntVec3 cursorPosition, Thing primaryThing)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Mineable regions list must contain at least one region");

            Thing = primaryThing; // Keep reference for def info
            TerrainRegions = regions;
            // Position is the center of the closest region
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = label;
            IsTerrain = false; // Mineables are Things, not terrain
        }

        // Constructor for deep ore deposit regions with quantity tracking
        public ScannerItem(List<TerrainRegion> regions, ThingDef oreDef, IntVec3 cursorPosition)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Deep ore regions list must contain at least one region");

            Thing = null;
            DeepOreDef = oreDef;
            TerrainRegions = regions;
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = "RimWorldAccess.Map.Scanner.DeepOre.Deposit".Translate(oreDef.label);
            IsTerrain = true; // Treat as terrain-like for navigation
        }

        // Constructor for captured Anomaly entities held on a holding platform. The held pawn
        // is not spawned on the map, so we take the platform's position for navigation while
        // keeping the pawn as Thing for label/announcement purposes.
        public ScannerItem(Pawn heldPawn, Building_HoldingPlatform platform, IntVec3 cursorPosition)
        {
            Thing = heldPawn;
            HoldingPlatform = platform;
            Position = platform.Position;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(heldPawn);
        }

        // Constructor for designation items
        public ScannerItem(Designation designation, IntVec3 cursorPosition)
        {
            Designation = designation;
            Position = designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = designation.target.HasThing ? designation.target.Thing : null;
            Label = ScannerLabelBuilder.BuildDesignationLabel(designation, Find.CurrentMap);
        }

        // Constructor for grouped designations (same type)
        public ScannerItem(List<Designation> designations, IntVec3 cursorPosition)
        {
            if (designations == null || designations.Count == 0)
                throw new ArgumentException("Designation group must contain at least one designation");

            BulkDesignations = designations;
            Designation = designations[0]; // Primary designation (closest)
            Position = Designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = Designation.target.HasThing ? Designation.target.Thing : null;

            // Get localized label from the Designator
            Label = ScannerHelper.GetLocalizedDesignationLabel(Designation.def);
        }

        // Constructor for zone items
        public ScannerItem(Zone zone, IntVec3 cursorPosition)
        {
            Zone = zone;
            IsTerrain = false;

            // Calculate center position of zone, ensuring it's within the zone for irregular shapes
            if (zone.cells != null && zone.cells.Count > 0)
            {
                int sumX = 0, sumZ = 0;
                foreach (var c in zone.cells) { sumX += c.x; sumZ += c.z; }
                int avgX = (int)Math.Round((double)sumX / zone.cells.Count);
                int avgZ = (int)Math.Round((double)sumZ / zone.cells.Count);
                var centerCandidate = new IntVec3(avgX, 0, avgZ);
                if (zone.cells.Contains(centerCandidate))
                {
                    Position = centerCandidate;
                }
                else
                {
                    IntVec3 closest = zone.cells[0];
                    float closestDist = float.MaxValue;
                    foreach (var c in zone.cells)
                    {
                        float dist = (c - centerCandidate).LengthHorizontal;
                        if (dist < closestDist) { closestDist = dist; closest = c; }
                    }
                    Position = closest;
                }
            }
            else
            {
                Position = zone.Position; // Fallback to first cell
            }

            Distance = (Position - cursorPosition).LengthHorizontal;
            Label = ScannerLabelBuilder.BuildZoneLabel(zone);
        }

        // Constructor for room items
        public ScannerItem(Room room, IntVec3 cursorPosition)
        {
            Room = room;
            IsTerrain = false;

            // Calculate center position of room, ensuring it's within the room for irregular shapes
            var cells = room.Cells.ToList();
            if (cells.Count > 0)
            {
                int sumX = 0, sumZ = 0;
                foreach (var c in cells) { sumX += c.x; sumZ += c.z; }
                int avgX = (int)Math.Round((double)sumX / cells.Count);
                int avgZ = (int)Math.Round((double)sumZ / cells.Count);
                var centerCandidate = new IntVec3(avgX, 0, avgZ);
                if (cells.Contains(centerCandidate))
                {
                    Position = centerCandidate;
                }
                else
                {
                    IntVec3 closest = cells[0];
                    float closestDist = float.MaxValue;
                    foreach (var c in cells)
                    {
                        float dist = (c - centerCandidate).LengthHorizontal;
                        if (dist < closestDist) { closestDist = dist; closest = c; }
                    }
                    Position = closest;
                }
            }
            else
            {
                Position = IntVec3.Zero;
            }

            Distance = (Position - cursorPosition).LengthHorizontal;
            Label = ScannerLabelBuilder.BuildRoomLabel(room);
        }

        /// <summary>
        /// Re-derives the label from the live game object.
        /// Call before announcing to get fresh labels without a full scanner refresh.
        /// Sets IsStale = true if the underlying Thing has been destroyed or despawned.
        /// </summary>
        public void RefreshLabel()
        {
            IsStale = false;

            if (IsCapturedEntity)
            {
                // Captured entity is not Spawned itself — its liveness is tied to the platform
                // still existing on the map and still holding this same pawn.
                if (HoldingPlatform == null || HoldingPlatform.Destroyed || !HoldingPlatform.Spawned
                    || HoldingPlatform.HeldPawn != Thing)
                {
                    IsStale = true;
                    return;
                }

                Label = ScannerLabelBuilder.BuildThingLabel(Thing);
            }
            else if (Thing != null)
            {
                if (Thing.Destroyed || !Thing.Spawned)
                {
                    IsStale = true;
                    return;
                }

                Label = ScannerLabelBuilder.BuildThingLabel(Thing);
            }
            else if (Zone != null)
            {
                Label = ScannerLabelBuilder.BuildZoneLabel(Zone);
            }
            else if (Room != null)
            {
                Label = ScannerLabelBuilder.BuildRoomLabel(Room);
            }
            // Terrain and designations: labels derived from defs, don't change
        }

    }

    public class ScannerSubcategory
    {
        public string Name { get; set; }
        public List<ScannerItem> Items { get; set; }

        public ScannerSubcategory(string name)
        {
            Name = name;
            Items = new List<ScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;
    }

    public class ScannerCategory
    {
        public string Name { get; set; }
        public List<ScannerSubcategory> Subcategories { get; set; }

        public ScannerCategory(string name)
        {
            Name = name;
            Subcategories = new List<ScannerSubcategory>();
        }

        /// <summary>
        /// Creates a ScannerCategory with an "All" subcategory pre-inserted at index 0.
        /// Items added to any specialized subcategory should also be added to Subcategories[0]
        /// (via the AddTo helper in ScannerHelper) so the "All" subcategory mirrors the whole category.
        /// </summary>
        public static ScannerCategory Create(string name)
        {
            var cat = new ScannerCategory(name);
            cat.Subcategories.Add(new ScannerSubcategory($"{name}-All"));
            return cat;
        }

        /// <summary>
        /// The "All" subcategory (convention: Subcategories[0]). Returns null if the category was
        /// built without Create() and has no All subcategory yet.
        /// </summary>
        public ScannerSubcategory AllSubcategory =>
            Subcategories != null && Subcategories.Count > 0 ? Subcategories[0] : null;

        public bool IsEmpty => Subcategories == null || Subcategories.All(sc => sc.IsEmpty);

        public int TotalItemCount => AllSubcategory?.Items.Count ?? 0;
    }

    public static class ScannerHelper
    {
        // Shared label for fog-of-war items in the Unexplored category. Search excludes items
        // with this label since every fog region carries the same name and would dominate
        // results. Used by both CollectMapItems (when emitting fog items) and the search
        // filter so they stay in sync. Returned as a localized property so comparisons in
        // ScannerSearchState remain correct across all languages.
        public static string UnexploredAreaLabel => (string)"RimWorldAccess.Map.Scanner.UnexploredArea".Translate();
        public static string PollutedAreaLabel => (string)"RimWorldAccess.Map.Scanner.PollutedArea".Translate();

        /// <summary>
        /// Adds a ScannerItem to both its specialized subcategory AND the category's "All"
        /// subcategory (convention: Subcategories[0]). This is the standard pattern used by
        /// CollectMapItems so the "All" subcategory mirrors every specialized subcategory.
        /// </summary>
        private static void AddTo(ScannerCategory category, ScannerSubcategory specialized, ScannerItem item)
        {
            specialized.Items.Add(item);
            category.Subcategories[0].Items.Add(item);
        }

        public static List<ScannerCategory> CollectMapItems(Map map, IntVec3 cursorPosition)
        {
            // Track all things that get categorized
            var categorizedThings = new HashSet<Thing>();

            // Build all scanner categories + subcategories from the declarative schema in
            // ScannerCategorySchemas.All. Every category gets an "All" subcategory at index 0.
            var buckets = ScannerBuckets.BuildFromSchema();

            // Extract named references for the specialized subcategories used during categorization.
            // These are one-shot dictionary lookups; the per-item work below uses these local refs.
            var pawnsCategory = buckets.Cat("Pawns");
            var pawnsColonistsSubcat = buckets.Sub("Pawns-Colonists");
            var pawnsPrisonersSubcat = buckets.Sub("Pawns-Prisoners");
            var pawnsSlavesSubcat = buckets.Sub("Pawns-Slaves");
            var pawnsGuestsSubcat = buckets.Sub("Pawns-Guests");
            var pawnsHostileSubcat = buckets.Sub("Pawns-Hostile");
            var pawnsPlayerMechSubcat = buckets.Sub("Pawns-Player Mechs");
            var pawnsHostileMechSubcat = buckets.Sub("Pawns-Hostile Mechs");

            var entitiesCategory = buckets.Cat("Entities");
            var entitiesHostileSubcat = buckets.Sub("Entities-Hostile");
            var entitiesCapturedSubcat = buckets.Sub("Entities-Captured");

            var tameAnimalsCategory = buckets.Cat("Tame");
            var tamePenSubcat = buckets.Sub("Tame-Pen");
            var tameNonPenSubcat = buckets.Sub("Tame-NonPen");

            var wildAnimalsCategory = buckets.Cat("Wild");
            var wildHostileSubcat = buckets.Sub("Wild-Hostile");
            var wildPassiveSubcat = buckets.Sub("Wild-Passive");

            var hazardsCategory = buckets.Cat("Hazards");
            var fireSubcat = buckets.Sub("Hazards-Fire");
            var blightSubcat = buckets.Sub("Hazards-Blight");

            var buildingsCategory = buckets.Cat("Buildings");
            var structureSubcat = buckets.Sub("Buildings-Structure");
            var productionSubcat = buckets.Sub("Buildings-Production");
            var furnitureSubcat = buckets.Sub("Buildings-Furniture");
            var powerSubcat = buckets.Sub("Buildings-Power");
            var securitySubcat = buckets.Sub("Buildings-Security");
            var miscBuildingsSubcat = buckets.Sub("Buildings-Misc");
            var recreationSubcat = buckets.Sub("Buildings-Recreation");
            var shipSubcat = buckets.Sub("Buildings-Ship");
            var temperatureSubcat = buckets.Sub("Buildings-Temperature");
            var travelingSubcat = buckets.Sub("Buildings-Traveling");

            var treesCategory = buckets.Cat("Trees");
            var harvestableTreesSubcat = buckets.Sub("Trees-Harvestable");
            var nonHarvestableTreesSubcat = buckets.Sub("Trees-NonHarvestable");

            var plantsCategory = buckets.Cat("Plants");
            var harvestablePlantsSubcat = buckets.Sub("Plants-Harvestable");
            var debrisSubcat = buckets.Sub("Plants-Debris");

            var itemsCategory = buckets.Cat("Items");
            var itemsStoredSubcat = buckets.Sub("Items-Stored");
            var itemsFurnitureSubcat = buckets.Sub("Items-Furniture");
            var itemsScatteredSubcat = buckets.Sub("Items-Scattered");
            var itemsForbiddenSubcat = buckets.Sub("Items-Forbidden");

            var terrainCategory = buckets.Cat("Terrain");
            var terrainNaturalSubcat = buckets.Sub("Terrain-Natural");
            var terrainConstructedSubcat = buckets.Sub("Terrain-Constructed");
            var terrainPollutedSubcat = buckets.Sub("Terrain-Polluted");

            var mineableCategory = buckets.Cat("Mineable");
            var mineableRareSubcat = buckets.Sub("Mineable-Rare");
            var mineableStoneSubcat = buckets.Sub("Mineable-Stone");
            var mineableChunksSubcat = buckets.Sub("Mineable-Chunks");
            var mineableScannedSubcat = buckets.Sub("Mineable-Scanned Ore");

            var ordersCategory = buckets.Cat("Orders");
            var ordersConstructionSubcat = buckets.Sub("Orders-Construction");
            var ordersHaulSubcat = buckets.Sub("Orders-Haul");
            var ordersHuntSubcat = buckets.Sub("Orders-Hunt");
            var ordersMineSubcat = buckets.Sub("Orders-Mine");
            var ordersDeconstructSubcat = buckets.Sub("Orders-Deconstruct");
            var ordersUninstallSubcat = buckets.Sub("Orders-Uninstall");
            var ordersCutSubcat = buckets.Sub("Orders-Cut");
            var ordersHarvestSubcat = buckets.Sub("Orders-Harvest");
            var ordersSmoothSubcat = buckets.Sub("Orders-Smooth");
            var ordersTameSubcat = buckets.Sub("Orders-Tame");
            var ordersSlaughterSubcat = buckets.Sub("Orders-Slaughter");
            var ordersOtherSubcat = buckets.Sub("Orders-Other");

            var zonesCategory = buckets.Cat("Zones");
            var zonesGrowingSubcat = buckets.Sub("Zones-Growing");
            var zonesStockpileSubcat = buckets.Sub("Zones-Stockpile");
            var zonesFishingSubcat = buckets.Sub("Zones-Fishing");
            var zonesOtherSubcat = buckets.Sub("Zones-Other");

            var roomsCategory = buckets.Cat("Rooms");
            var unexploredCategory = buckets.Cat("Unexplored");

            // Uncategorized category — dict of per-def subcategories. The "All" subcategory
            // at index 0 was inserted by BuildFromSchema.
            var uncategorizedCategory = buckets.Cat("Uncategorized");
            var uncategorizedByDef = new Dictionary<string, ScannerSubcategory>();

            // Collect all things from the map
            var allThings = map.listerThings.AllThings;
            var playerFaction = Faction.OfPlayer;
            var fogGrid = map.fogGrid;

            foreach (var thing in allThings)
            {
                if (!thing.Spawned || !thing.Position.IsValid)
                    continue;

                // Skip items in fog of war (unseen tiles)
                if (fogGrid.IsFogged(thing.Position))
                    continue;

                var item = new ScannerItem(thing, cursorPosition);

                if (thing is Pawn pawn)
                {
                    // IsHiddenFromPlayer exempts player-faction pawns, so invisibility psycasts
                    // on colonists still surface in the scanner; only hostile stealth is filtered.
                    if (pawn.IsHiddenFromPlayer())
                        continue;

                    // Anomaly entities are permanent enemies of all non-Insect factions, so any
                    // loose entity on the map is hostile by definition.
                    if (pawn.RaceProps.IsAnomalyEntity)
                    {
                        AddTo(entitiesCategory, entitiesHostileSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    // Categorize pawns by faction relationship (7-bucket scheme).
                    else if (pawn.RaceProps.IsMechanoid)
                    {
                        if (pawn.Faction == playerFaction)
                        {
                            AddTo(pawnsCategory, pawnsPlayerMechSubcat, item);
                        }
                        else if (pawn.HostileTo(Faction.OfPlayer))
                        {
                            AddTo(pawnsCategory, pawnsHostileMechSubcat, item);
                        }
                        else
                        {
                            // Neutral mechs fold into Guests alongside other helpful neutrals.
                            AddTo(pawnsCategory, pawnsGuestsSubcat, item);
                        }
                        categorizedThings.Add(thing);
                    }
                    else if (pawn.RaceProps.Humanlike)
                    {
                        // Dispatch by relationship role so raids don't get jumbled with friendly visitors.
                        if (pawn.IsColonist)
                            AddTo(pawnsCategory, pawnsColonistsSubcat, item);
                        else if (pawn.IsPrisonerOfColony)
                            AddTo(pawnsCategory, pawnsPrisonersSubcat, item);
                        else if (pawn.IsSlaveOfColony)
                            AddTo(pawnsCategory, pawnsSlavesSubcat, item);
                        else if (pawn.HostileTo(Faction.OfPlayer))
                            AddTo(pawnsCategory, pawnsHostileSubcat, item);
                        else
                            // Visitors, traders, quest lodgers, allied raid help, neutral factions.
                            AddTo(pawnsCategory, pawnsGuestsSubcat, item);

                        categorizedThings.Add(thing);
                    }
                    else if (pawn.RaceProps.Animal)
                    {
                        // Animals
                        if (pawn.Faction == playerFaction)
                        {
                            // Tame animals - check if pen animal (roamer = needs to be managed by rope)
                            if (pawn.Roamer)
                            {
                                AddTo(tameAnimalsCategory, tamePenSubcat, item);
                                categorizedThings.Add(thing);
                            }
                            else
                            {
                                AddTo(tameAnimalsCategory, tameNonPenSubcat, item);
                                categorizedThings.Add(thing);
                            }
                        }
                        else
                        {
                            // Wild animals - check if hostile
                            if (pawn.HostileTo(playerFaction))
                            {
                                AddTo(wildAnimalsCategory, wildHostileSubcat, item);
                                categorizedThings.Add(thing);
                            }
                            else
                            {
                                AddTo(wildAnimalsCategory, wildPassiveSubcat, item);
                                categorizedThings.Add(thing);
                            }
                        }
                    }
                }
                else if (thing is Fire)
                {
                    // Fire hazard
                    AddTo(hazardsCategory, fireSubcat, item);
                    categorizedThings.Add(thing);
                }
                else if (thing is Plant plant)
                {
                    // Blighted plants ALSO appear in Hazards-Blight (and Hazards-All).
                    // The plant's primary "All" is Plants/Trees, added via AddTo below.
                    if (plant.Blighted)
                    {
                        blightSubcat.Items.Add(item);
                        hazardsCategory.Subcategories[0].Items.Add(item);
                    }

                    if (plant.def.plant.IsTree)
                    {
                        // Trees: only "Harvestable" if fully mature and ready to harvest
                        if (plant.HarvestableNow && plant.LifeStage == PlantLifeStage.Mature)
                        {
                            AddTo(treesCategory, harvestableTreesSubcat, item);
                            categorizedThings.Add(thing);
                        }
                        else
                        {
                            AddTo(treesCategory, nonHarvestableTreesSubcat, item);
                            categorizedThings.Add(thing);
                        }
                    }
                    else
                    {
                        // Non-tree plants: only "Harvestable" if fully mature and ready to harvest
                        if (plant.HarvestableNow && plant.LifeStage == PlantLifeStage.Mature)
                        {
                            AddTo(plantsCategory, harvestablePlantsSubcat, item);
                            categorizedThings.Add(thing);
                        }
                        else
                        {
                            // Not fully mature (includes grass, immature crops, etc.)
                            AddTo(plantsCategory, debrisSubcat, item);
                            categorizedThings.Add(thing);
                        }
                    }
                }
                else if (thing is Blueprint || thing is Frame)
                {
                    // Blueprints and frames (construction projects) go to Orders-Construction
                    AddTo(ordersCategory, ordersConstructionSubcat, item);
                    categorizedThings.Add(thing);
                }
                else if (thing is Building building)
                {
                    // Skip natural rock/ore (these are handled as mineable tiles below)
                    if (building.def.building != null && building.def.building.isNaturalRock)
                        continue;

                    // Check for travel-related buildings first (before designation category)
                    if (IsTravelingBuilding(building))
                    {
                        AddTo(buildingsCategory, travelingSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    else
                    {
                        // Categorize buildings by designation category
                        var designationCategory = building.def.designationCategory;
                        ScannerSubcategory targetBuildingSub = structureSubcat; // default
                        if (designationCategory != null)
                        {
                            switch (designationCategory.defName)
                            {
                                case "Structure": targetBuildingSub = structureSubcat; break;
                                case "Production": targetBuildingSub = productionSubcat; break;
                                case "Furniture": targetBuildingSub = furnitureSubcat; break;
                                case "Power": targetBuildingSub = powerSubcat; break;
                                case "Security": targetBuildingSub = securitySubcat; break;
                                case "Misc": targetBuildingSub = miscBuildingsSubcat; break;
                                case "Joy": targetBuildingSub = recreationSubcat; break;
                                case "Ship": targetBuildingSub = shipSubcat; break;
                                case "Temperature": targetBuildingSub = temperatureSubcat; break;
                                default: targetBuildingSub = structureSubcat; break;
                            }
                        }
                        AddTo(buildingsCategory, targetBuildingSub, item);
                        categorizedThings.Add(thing);
                    }
                }
                else if (IsStoneChunk(thing))
                {
                    // Stone chunks go to mineable chunks subcategory
                    AddTo(mineableCategory, mineableChunksSubcat, item);
                    categorizedThings.Add(thing);
                }
                else if (!IsDebrisItem(thing))
                {
                    // Regular items - categorize by storage state
                    if (thing.IsForbidden(Faction.OfPlayer))
                    {
                        AddTo(itemsCategory, itemsForbiddenSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    else if (IsUninstalledFurniture(thing))
                    {
                        // Uninstalled furniture
                        AddTo(itemsCategory, itemsFurnitureSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    else if (IsInStorage(thing, map))
                    {
                        // Items in stockpiles/shelves
                        AddTo(itemsCategory, itemsStoredSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    else
                    {
                        // Scattered items not in storage
                        AddTo(itemsCategory, itemsScatteredSubcat, item);
                        categorizedThings.Add(thing);
                    }
                }

                // If we reach here without categorizing, this is an uncategorized item.
                // Uncategorized-All mirrors every per-def subcategory.
                if (!categorizedThings.Contains(thing) && thing.def.selectable)
                {
                    string subcatName = thing.def.label ?? thing.def.defName;
                    if (!uncategorizedByDef.ContainsKey(subcatName))
                    {
                        var newSubcat = new ScannerSubcategory($"Uncategorized-{subcatName}");
                        uncategorizedByDef[subcatName] = newSubcat;
                        uncategorizedCategory.Subcategories.Add(newSubcat);
                    }
                    uncategorizedByDef[subcatName].Items.Add(item);
                    uncategorizedCategory.Subcategories[0].Items.Add(item); // "All"
                }
            }

            // Collect captured Anomaly entities from holding platforms. Held pawns live inside
            // the platform's innerContainer and are not in map.listerThings.AllThings, so we walk
            // platforms explicitly. AllBuildingsColonistOfClass catches both HoldingPlatform and
            // HoldingSpot variants (both derive from Building_HoldingPlatform).
            foreach (var holdingPlatform in map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>())
            {
                if (!holdingPlatform.Spawned || fogGrid.IsFogged(holdingPlatform.Position))
                    continue;

                var heldPawn = holdingPlatform.HeldPawn;
                if (heldPawn == null || !heldPawn.RaceProps.IsAnomalyEntity)
                    continue;

                var capturedItem = new ScannerItem(heldPawn, holdingPlatform, cursorPosition);
                AddTo(entitiesCategory, entitiesCapturedSubcat, capturedItem);
            }

            // Collect mineable tiles, terrain, deep ore, and fog cells in a single pass
            // over all cells. The cache is invalidated on building changes (cell hash) OR on
            // any fog state change — fog collection shares this walk, so all four categories
            // refresh together.
            int currentCellHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.BuildingArtificial);

            // Pollution (Biotech) is a per-cell grid overlay, not a TerrainDef, and changes
            // independently of building/fog state. TotalPollution (BoolGrid.TrueCount) is an
            // O(1) counter, so fold it into the cache key to refresh polluted patches without
            // forcing rescans for non-Biotech maps (count stays 0).
            int currentPollutionCount = ModsConfig.BiotechActive ? map.pollutionGrid.TotalPollution : 0;

            if (cachedTerrainNatural != null && currentCellHash == lastCellHash && !fogDirty
                && currentPollutionCount == lastPollutionCount)
            {
                // Reuse cached cell data — skip 60K+ cell iteration entirely.
                // Also mirror every cached item into the category's "All" subcategory.
                terrainNaturalSubcat.Items.AddRange(cachedTerrainNatural);
                terrainConstructedSubcat.Items.AddRange(cachedTerrainConstructed);
                terrainPollutedSubcat.Items.AddRange(cachedPollutedItems);
                mineableRareSubcat.Items.AddRange(cachedMineableRare);
                mineableStoneSubcat.Items.AddRange(cachedMineableStone);
                mineableScannedSubcat.Items.AddRange(cachedMineableScanned);
                unexploredCategory.Subcategories[0].Items.AddRange(cachedFogItems);

                terrainCategory.Subcategories[0].Items.AddRange(cachedTerrainNatural);
                terrainCategory.Subcategories[0].Items.AddRange(cachedTerrainConstructed);
                terrainCategory.Subcategories[0].Items.AddRange(cachedPollutedItems);
                mineableCategory.Subcategories[0].Items.AddRange(cachedMineableRare);
                mineableCategory.Subcategories[0].Items.AddRange(cachedMineableStone);
                mineableCategory.Subcategories[0].Items.AddRange(cachedMineableScanned);
            }
            else
            {
                var allCells = map.AllCells;
                bool hasDeepScanner = map.deepResourceGrid.AnyActiveDeepScannersOnMap();
                var deepOreByDef = new Dictionary<string, List<(IntVec3 position, int count, ThingDef oreDef)>>();

                // Collect mineables by def type for later adjacency grouping
                var mineableRareByDef = new Dictionary<string, List<(IntVec3 position, Thing thing)>>();
                var mineableStoneByDef = new Dictionary<string, List<(IntVec3 position, Thing thing)>>();
                var fogPositions = new List<IntVec3>();
                var pollutedPositions = new List<IntVec3>();

                foreach (var cell in allCells)
                {
                    // Fog-dependent collection: mineables and terrain
                    if (!fogGrid.IsFogged(cell))
                    {
                        var terrain = map.terrainGrid.TerrainAt(cell);

                        // Polluted cells (Biotech) — a per-cell overlay independent of the
                        // terrain def, gathered here for adjacency grouping below so the
                        // player can jump to each contaminated patch and clean it.
                        if (ModsConfig.BiotechActive && map.pollutionGrid.IsPolluted(cell))
                            pollutedPositions.Add(cell);

                        // Check for mineable rocks (both ore and plain stone)
                        var edifice = cell.GetEdifice(map);
                        if (edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock)
                        {
                            string defKey = edifice.def.defName;

                            // Separate rare minerals (ore) from plain stone
                            if (edifice.def.building.isResourceRock && edifice.def.building.mineableYield > 0)
                            {
                                // Rare minerals (steel, gold, plasteel, uranium, etc.)
                                if (!mineableRareByDef.ContainsKey(defKey))
                                    mineableRareByDef[defKey] = new List<(IntVec3, Thing)>();
                                mineableRareByDef[defKey].Add((cell, edifice));
                                categorizedThings.Add(edifice);
                            }
                            else
                            {
                                // Plain stone (granite, marble, slate, limestone, sandstone)
                                if (!mineableStoneByDef.ContainsKey(defKey))
                                    mineableStoneByDef[defKey] = new List<(IntVec3, Thing)>();
                                mineableStoneByDef[defKey].Add((cell, edifice));
                                categorizedThings.Add(edifice);
                            }
                        }

                        // Collect terrain tiles
                        if (terrain != null)
                        {
                            // Natural terrain — include anything that is NOT plain default soil.
                            // Property-based detection: plain soil (Soil, GrasslandSoil, GlowforestSoil)
                            // has fertility=1.0 AND pathCost<=2. Anything with non-default fertility
                            // (mud=0, rich=1.4, gravel=0.7, sand=0.1, etc.) or elevated path cost
                            // (mud=14, sand=4, water=30/300, moss=3) is interesting. This catches
                            // mud, moss, riverbank, volcanic rock, lava, flesh, space, and all
                            // DLC natural terrain variants without fragile defName string-matching.
                            if (!terrain.layerable && terrain.natural)
                            {
                                bool isInteresting =
                                    terrain.fertility != 1.0f ||
                                    terrain.pathCost > 2;
                                if (isInteresting)
                                {
                                    var terrainItem = new ScannerItem(cell, terrain.label, cursorPosition);
                                    AddTo(terrainCategory, terrainNaturalSubcat, terrainItem);
                                }
                            }
                            // Constructed floors
                            else if (terrain.layerable || !terrain.natural)
                            {
                                // Only include actually constructed floors (not natural dirt/soil)
                                if (!terrain.natural)
                                {
                                    var terrainItem = new ScannerItem(cell, terrain.label, cursorPosition);
                                    AddTo(terrainCategory, terrainConstructedSubcat, terrainItem);
                                }
                            }
                        }
                    }
                    else
                    {
                        fogPositions.Add(cell);
                    }

                    // Collect deep ore in same pass (only if active scanner exists)
                    // Deep ore is underground - no fog check needed (matches RimWorld's behavior)
                    if (hasDeepScanner)
                    {
                        var oreDef = map.deepResourceGrid.ThingDefAt(cell);
                        if (oreDef != null)
                        {
                            int count = map.deepResourceGrid.CountAt(cell);
                            if (count > 0)
                            {
                                string defKey = oreDef.defName;
                                if (!deepOreByDef.ContainsKey(defKey))
                                    deepOreByDef[defKey] = new List<(IntVec3, int, ThingDef)>();
                                deepOreByDef[defKey].Add((cell, count, oreDef));
                            }
                        }
                    }
                }

                // Group rare mineables (ore) by adjacency
                foreach (var kvp in mineableRareByDef)
                {
                    var positions = kvp.Value.Select(x => x.position).ToList();
                    var regions = GroupTerrainByAdjacency(positions, cursorPosition);
                    var primaryThing = kvp.Value[0].thing;
                    string label = primaryThing.def.label ?? (string)"RimWorldAccess.Map.Label.Unknown".Translate();

                    // Create item with regions (like terrain does)
                    var item = new ScannerItem(regions, label, cursorPosition, primaryThing);
                    AddTo(mineableCategory, mineableRareSubcat, item);
                }

                // Group stone mineables by adjacency
                foreach (var kvp in mineableStoneByDef)
                {
                    var positions = kvp.Value.Select(x => x.position).ToList();
                    var regions = GroupTerrainByAdjacency(positions, cursorPosition);
                    var primaryThing = kvp.Value[0].thing;
                    string label = primaryThing.def.label ?? (string)"RimWorldAccess.Map.Label.Unknown".Translate();

                    // Create item with regions (like terrain does)
                    var item = new ScannerItem(regions, label, cursorPosition, primaryThing);
                    AddTo(mineableCategory, mineableStoneSubcat, item);
                }

                // Group deep ore by adjacency and create scanner items (collected during cell loop above)
                if (hasDeepScanner)
                {
                    foreach (var kvp in deepOreByDef)
                    {
                        var positionsWithCounts = kvp.Value.Select(x => (x.position, x.count)).ToList();
                        var oreDef = kvp.Value[0].oreDef;
                        var regions = GroupDeepOreByAdjacency(positionsWithCounts, cursorPosition);

                        if (regions.Count > 0)
                        {
                            var item = new ScannerItem(regions, oreDef, cursorPosition);
                            AddTo(mineableCategory, mineableScannedSubcat, item);
                        }
                    }
                }

                // Group unexplored fog cells by adjacency. Each contiguous fog region becomes
                // its own scanner item so users can navigate region-by-region. This matches the
                // game's data model: FogGrid is a sibling of TerrainGrid on Map, not a property
                // of terrain, so each fog blob is treated as a first-class navigable item.
                // Unexplored has only an "All" subcategory, so add directly (AddTo would
                // double-add since specialized == Subcategories[0]).
                var fogRegions = GroupTerrainByAdjacency(fogPositions, cursorPosition);
                foreach (var region in fogRegions)
                {
                    var fogItem = new ScannerItem(
                        new List<TerrainRegion> { region }, UnexploredAreaLabel, cursorPosition);
                    unexploredCategory.Subcategories[0].Items.Add(fogItem);
                }

                // Group polluted cells into contiguous patches (Biotech). Each patch becomes a
                // navigable item under Terrain > Polluted so the player can jump to it and place
                // a pollution removal area. pollutedPositions is empty without Biotech.
                foreach (var region in GroupTerrainByAdjacency(pollutedPositions, cursorPosition))
                {
                    var pollutedItem = new ScannerItem(
                        new List<TerrainRegion> { region }, PollutedAreaLabel, cursorPosition);
                    AddTo(terrainCategory, terrainPollutedSubcat, pollutedItem);
                }

                // Save results to cell cache
                cachedTerrainNatural = new List<ScannerItem>(terrainNaturalSubcat.Items);
                cachedTerrainConstructed = new List<ScannerItem>(terrainConstructedSubcat.Items);
                cachedPollutedItems = new List<ScannerItem>(terrainPollutedSubcat.Items);
                cachedMineableRare = new List<ScannerItem>(mineableRareSubcat.Items);
                cachedMineableStone = new List<ScannerItem>(mineableStoneSubcat.Items);
                cachedMineableScanned = new List<ScannerItem>(mineableScannedSubcat.Items);
                cachedFogItems = new List<ScannerItem>(unexploredCategory.Subcategories[0].Items);
                fogDirty = false;

                lastCellHash = currentCellHash;
                lastPollutionCount = currentPollutionCount;
            }

            // Collect all designations/orders
            var allDesignations = map.designationManager.AllDesignations;
            foreach (var designation in allDesignations)
            {
                // Skip designations without valid targets
                if (designation == null || designation.def == null)
                    continue;

                // Skip if target cell is invalid or fogged
                IntVec3 targetCell = designation.target.Cell;
                if (!targetCell.IsValid || fogGrid.IsFogged(targetCell))
                    continue;

                // Skip if thing target is not spawned
                if (designation.target.HasThing && !designation.target.Thing.Spawned)
                    continue;

                var item = new ScannerItem(designation, cursorPosition);

                // Categorize by designation type
                ScannerSubcategory orderSub;
                if (designation.def == DesignationDefOf.Haul)
                    orderSub = ordersHaulSubcat;
                else if (designation.def == DesignationDefOf.Hunt)
                    orderSub = ordersHuntSubcat;
                else if (designation.def == DesignationDefOf.Mine || designation.def == DesignationDefOf.MineVein)
                    orderSub = ordersMineSubcat;
                else if (designation.def == DesignationDefOf.Deconstruct)
                    orderSub = ordersDeconstructSubcat;
                else if (designation.def == DesignationDefOf.Uninstall)
                    orderSub = ordersUninstallSubcat;
                else if (designation.def == DesignationDefOf.CutPlant || designation.def == DesignationDefOf.ExtractTree)
                    orderSub = ordersCutSubcat;
                else if (designation.def == DesignationDefOf.HarvestPlant)
                    orderSub = ordersHarvestSubcat;
                else if (designation.def == DesignationDefOf.SmoothFloor || designation.def == DesignationDefOf.SmoothWall)
                    orderSub = ordersSmoothSubcat;
                else if (designation.def == DesignationDefOf.Tame)
                    orderSub = ordersTameSubcat;
                else if (designation.def == DesignationDefOf.Slaughter)
                    orderSub = ordersSlaughterSubcat;
                else
                    // All other designations (Strip, Open, Flick, RemoveFloor, etc.)
                    orderSub = ordersOtherSubcat;

                AddTo(ordersCategory, orderSub, item);
            }

            // Collect all zones - filter to non-empty zones
            var validZones = map.zoneManager.AllZones.Where(zone =>
                zone != null && zone.cells != null && zone.cells.Count > 0);

            foreach (var zone in validZones)
            {
                var item = new ScannerItem(zone, cursorPosition);

                ScannerSubcategory zoneSub;
                if (zone is Zone_Growing)
                    zoneSub = zonesGrowingSubcat;
                else if (zone is Zone_Stockpile)
                    zoneSub = zonesStockpileSubcat;
                else if (zone.GetType().Name == "Zone_Fishing")
                    zoneSub = zonesFishingSubcat;
                else
                    zoneSub = zonesOtherSubcat;

                AddTo(zonesCategory, zoneSub, item);
            }

            // Collect all rooms - filter to indoor, proper rooms with at least one visible cell
            var visibleIndoorRooms = map.regionGrid.AllRooms.Where(room =>
                !room.PsychologicallyOutdoors &&
                room.ProperRoom &&
                room.Cells.Any(cell => !fogGrid.IsFogged(cell)));

            // Rooms only has an "All" subcategory (no specialized buckets), so add directly to it.
            roomsCategory.Subcategories[0].Items.AddRange(
                visibleIndoorRooms.Select(room => new ScannerItem(room, cursorPosition)));

            // Build the top-level "All" category by flattening every other category's "-All"
            // subcategory, deduplicating by ScannerItem reference. Items that span multiple
            // categories (e.g., a blighted plant that appears in both Plants-All and Hazards-All)
            // appear only once in All-All.
            var allCategory = buckets.Cat("All");
            var allSubcat = allCategory.Subcategories[0];
            var seenInAll = new HashSet<ScannerItem>();
            foreach (var category in buckets.Categories)
            {
                if (category == allCategory) continue;
                if (category.Subcategories.Count == 0) continue;
                foreach (var item in category.Subcategories[0].Items) // Subcategories[0] == "{Name}-All"
                {
                    if (seenInAll.Add(item))
                        allSubcat.Items.Add(item);
                }
            }

            // Group identical items and sort all subcategories by distance.
            // Iterates every category from the schema-built bucket list (including Uncategorized).
            foreach (var category in buckets.Categories)
            {
                foreach (var subcat in category.Subcategories)
                {
                    // First sort by distance
                    subcat.Items = subcat.Items.OrderBy(i => i.Distance).ToList();

                    // Then group identical items (but not pawns - they're always unique)
                    subcat.Items = GroupIdenticalItems(subcat.Items, cursorPosition);
                }
            }

            // Remove empty categories — schema-declared categories that have no items on this map.
            var finalCategories = buckets.Categories;
            finalCategories.RemoveAll(c => c.IsEmpty);

            return finalCategories;
        }

        private static bool IsInStorage(Thing thing, Map map)
        {
            // Check if thing is in a stockpile zone
            var zone = map.zoneManager.ZoneAt(thing.Position);
            if (zone is Zone_Stockpile)
                return true;

            // Check if thing is on a storage building (shelf, rack, etc.)
            var storageBuilding = thing.Position.GetThingList(map)
                .OfType<Building_Storage>()
                .FirstOrDefault();

            return storageBuilding != null;
        }

        /// <summary>
        /// Checks if a building is travel-related (transport pods, launchers, hitching spots, shuttles).
        /// Fueling ports are only included if they don't have a pod connected (to avoid redundancy).
        /// </summary>
        private static bool IsTravelingBuilding(Building building)
        {
            if (building == null)
                return false;

            string defName = building.def.defName;

            // Transport pods and launchers
            if (defName.Contains("TransportPod") || defName.Contains("DropPod"))
                return true;

            // Pod launcher / fueling port - only if no pod is connected
            if (building.def.building != null && building.def.building.hasFuelingPort)
            {
                // Check if this fueling port has a connected transport pod
                // If it does, skip it (the pod will be listed instead)
                IntVec3 fuelingCell = FuelingPortUtility.GetFuelingPortCell(building);
                if (fuelingCell.IsValid && building.Map != null)
                {
                    // Check if there's a launchable (transport pod) at the fueling cell
                    CompLaunchable launchable = FuelingPortUtility.LaunchableAt(fuelingCell, building.Map);
                    if (launchable != null)
                    {
                        // Pod is connected - don't list the fueling port separately
                        return false;
                    }
                }
                // No pod connected - list the empty fueling port
                return true;
            }

            // Caravan hitching/packing spot
            if (defName.Contains("CaravanPackingSpot") || defName.Contains("HitchingSpot"))
                return true;

            // Shuttles (Royalty DLC)
            if (defName.Contains("Shuttle"))
                return true;

            // Check for CompTransporter or CompLaunchable components (catches modded variants)
            if (building is ThingWithComps twc2)
            {
                if (twc2.GetComp<CompTransporter>() != null || twc2.GetComp<CompLaunchable>() != null)
                    return true;
            }

            return false;
        }

        private static bool IsUninstalledFurniture(Thing thing)
        {
            // Check if it's a minified (uninstalled) building
            if (thing is MinifiedThing)
                return true;

            // Check if the thing def is a building that can be reinstalled
            if (thing.def.Minifiable)
                return true;

            return false;
        }

        private static bool IsStoneChunk(Thing thing)
        {
            // Check if this is a stone chunk (mineable resource lying on ground)
            if (thing.def.defName.Contains("Chunk"))
                return true;

            // Also check thingCategories for StoneChunks
            if (thing.def.thingCategories != null)
            {
                foreach (var cat in thing.def.thingCategories)
                {
                    if (cat.defName.Contains("Chunk"))
                        return true;
                }
            }

            return false;
        }

        private static bool IsDebrisItem(Thing thing)
        {
            // Check for common debris types
            if (thing.def.category == ThingCategory.Filth)
                return true;

            // Note: Chunks are now handled by IsStoneChunk, not filtered as debris

            if (thing.def.defName == "Slag")
                return true;

            // Check for rubble-like items
            var label = thing.def.label?.ToLower() ?? "";
            if (label.Contains("rubble") || label.Contains("slag"))
                return true;

            return false;
        }

        /// <summary>
        /// Yields the 8 cardinal + diagonal neighbors of a cell on the square map grid. The shared
        /// <see cref="Clump"/> flood-fill gates each neighbor on the valid-position set, so this
        /// only needs to enumerate candidate offsets.
        /// </summary>
        private static IEnumerable<IntVec3> EightWayNeighbors(IntVec3 cell)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    yield return new IntVec3(cell.x + dx, 0, cell.z + dz);
                }
            }
        }

        /// <summary>
        /// Performs a flood fill to find all contiguous positions starting from a given position.
        /// Uses 8-way adjacency (cardinal + diagonal). Delegates to the shared coordinate-agnostic
        /// <see cref="Clump.Fill{TTile}"/> so the local and world scanners share one implementation.
        /// </summary>
        /// <param name="startPos">The starting position for the flood fill</param>
        /// <param name="validPositions">Set of all valid positions to consider (must be of same terrain type)</param>
        /// <returns>Set of all contiguous positions found</returns>
        internal static HashSet<IntVec3> FloodFillTerrainRegion(IntVec3 startPos, HashSet<IntVec3> validPositions)
        {
            return Clump.Fill(startPos, validPositions, EightWayNeighbors);
        }

        /// <summary>
        /// Groups terrain positions by adjacency into separate regions.
        /// </summary>
        /// <param name="positions">All positions with the same terrain label</param>
        /// <param name="cursorPosition">Current cursor position for distance calculation</param>
        /// <returns>List of TerrainRegion objects sorted by distance from cursor</returns>
        internal static List<TerrainRegion> GroupTerrainByAdjacency(List<IntVec3> positions, IntVec3 cursorPosition)
        {
            // Group contiguous tiles via the shared flood-fill, wrap each set into a TerrainRegion
            // (which computes its center/dimensions/distance), then sort by distance from cursor.
            return Clump.GroupByAdjacency(positions, EightWayNeighbors)
                .Select(set => new TerrainRegion(set.ToList(), cursorPosition))
                .OrderBy(r => r.Distance)
                .ToList();
        }

        /// <summary>
        /// Groups deep ore positions by adjacency into separate regions, tracking quantity per region.
        /// </summary>
        /// <param name="positionsWithCounts">All positions with their ore counts</param>
        /// <param name="cursorPosition">Current cursor position for distance calculation</param>
        /// <returns>List of TerrainRegion objects with TotalQuantity populated, sorted by distance</returns>
        private static List<TerrainRegion> GroupDeepOreByAdjacency(
            List<(IntVec3 position, int count)> positionsWithCounts,
            IntVec3 cursorPosition)
        {
            var regions = new List<TerrainRegion>();
            var positionToCount = positionsWithCounts.ToDictionary(p => p.position, p => p.count);
            var remaining = new HashSet<IntVec3>(positionsWithCounts.Select(p => p.position));

            while (remaining.Count > 0)
            {
                // Start flood fill from the first remaining position
                var startPos = remaining.First();
                var regionPositions = FloodFillTerrainRegion(startPos, remaining);

                if (regionPositions.Count > 0)
                {
                    // Build list with counts for this region
                    var regionWithCounts = regionPositions
                        .Select(pos => (pos, positionToCount[pos]))
                        .ToList();

                    var region = new TerrainRegion(regionWithCounts, cursorPosition);
                    regions.Add(region);

                    // Remove processed positions
                    foreach (var pos in regionPositions)
                        remaining.Remove(pos);
                }
            }

            // Sort regions by distance from cursor
            return regions.OrderBy(r => r.Distance).ToList();
        }

        /// <summary>
        /// Groups identical items together (same def, quality, stuff).
        /// Pawns are never grouped - they're unique individuals.
        /// Terrain tiles are grouped by adjacency into separate regions.
        /// Designations are grouped by designation type.
        /// </summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            var grouped = new List<ScannerItem>();

            // Separate items by type for dictionary-based grouping
            var terrainByLabel = new Dictionary<string, List<ScannerItem>>();
            var designationsByDef = new Dictionary<DesignationDef, List<ScannerItem>>();
            var thingsByKey = new Dictionary<(ThingDef def, ThingDef stuff, QualityCategory? quality), List<ScannerItem>>();
            var passthrough = new List<ScannerItem>(); // items that don't get grouped

            // Single pass: categorize all items into buckets
            foreach (var item in items)
            {
                // Items with terrain regions are already grouped - pass through
                if (item.HasTerrainRegions)
                {
                    passthrough.Add(item);
                }
                // Terrain items: group by label
                else if (item.IsTerrain)
                {
                    if (!terrainByLabel.ContainsKey(item.Label))
                        terrainByLabel[item.Label] = new List<ScannerItem>();
                    terrainByLabel[item.Label].Add(item);
                }
                // Designation items: group by designation def
                else if (item.IsDesignation)
                {
                    var def = item.Designation.def;
                    if (!designationsByDef.ContainsKey(def))
                        designationsByDef[def] = new List<ScannerItem>();
                    designationsByDef[def].Add(item);
                }
                // Zones and rooms are unique - pass through
                else if (item.IsZone || item.IsRoom)
                {
                    passthrough.Add(item);
                }
                // Pawns are unique individuals - pass through
                else if (item.Thing is Pawn)
                {
                    passthrough.Add(item);
                }
                // Regular things: group by (def, stuff, quality)
                else if (item.Thing != null)
                {
                    var actualThing = GetActualThing(item.Thing);
                    var quality = actualThing.TryGetComp<CompQuality>()?.Quality;
                    var key = (actualThing.def, actualThing.Stuff, quality);
                    if (!thingsByKey.ContainsKey(key))
                        thingsByKey[key] = new List<ScannerItem>();
                    thingsByKey[key].Add(item);
                }
                else
                {
                    passthrough.Add(item);
                }
            }

            // Process terrain groups: adjacency grouping per label
            foreach (var kvp in terrainByLabel)
            {
                var positions = kvp.Value.Select(i => i.Position).ToList();
                var regions = GroupTerrainByAdjacency(positions, cursorPosition);

                if (regions.Count > 0)
                {
                    grouped.Add(new ScannerItem(regions, kvp.Key, cursorPosition));
                }
                else if (positions.Count == 1)
                {
                    grouped.Add(kvp.Value[0]);
                }
            }

            // Process designation groups
            foreach (var kvp in designationsByDef)
            {
                if (kvp.Value.Count > 1)
                {
                    var designations = kvp.Value.Select(i => i.Designation).ToList();
                    designations = designations.OrderBy(d => (d.target.Cell - cursorPosition).LengthHorizontal).ToList();
                    grouped.Add(new ScannerItem(designations, cursorPosition));
                }
                else
                {
                    grouped.Add(kvp.Value[0]);
                }
            }

            // Process thing groups
            foreach (var kvp in thingsByKey)
            {
                if (kvp.Value.Count > 1)
                {
                    var things = kvp.Value.Select(i => i.Thing).ToList();
                    things = things.OrderBy(t => (t.Position - cursorPosition).LengthHorizontal).ToList();
                    grouped.Add(new ScannerItem(things, cursorPosition));
                }
                else
                {
                    grouped.Add(kvp.Value[0]);
                }
            }

            // Add all passthrough items
            grouped.AddRange(passthrough);

            return grouped;
        }

        /// <summary>
        /// Unwraps a MinifiedThing to get the actual inner item, or returns the thing as-is.
        /// Handles MinifiedThing and MinifiedTree (which extends MinifiedThing).
        /// </summary>
        private static Thing GetActualThing(Thing thing)
        {
            if (thing is MinifiedThing minified && minified.InnerThing != null)
                return minified.InnerThing;
            return thing;
        }

        /// <summary>
        /// Checks if two things are identical (same def, quality, stuff, etc.)
        /// HP differences are ignored to prevent duplicate entries for damaged items.
        /// </summary>
        private static bool AreThingsIdentical(Thing a, Thing b)
        {
            // Unwrap minified things to compare actual items
            var actualA = GetActualThing(a);
            var actualB = GetActualThing(b);

            // Must be the same def
            if (actualA.def != actualB.def)
                return false;

            // Must have same stuff (material)
            if (actualA.Stuff != actualB.Stuff)
                return false;

            // Check quality if applicable
            var qualityA = actualA.TryGetComp<CompQuality>();
            var qualityB = actualB.TryGetComp<CompQuality>();

            if (qualityA != null && qualityB != null)
            {
                if (qualityA.Quality != qualityB.Quality)
                    return false;
            }
            else if (qualityA != null || qualityB != null)
            {
                // One has quality, the other doesn't
                return false;
            }

            // HP is now ignored - damaged trees, walls, etc. are grouped together
            return true;
        }

        // Cell-based collection cache (terrain, mineables, deep ore)
        private static List<ScannerItem> cachedTerrainNatural = null;
        private static List<ScannerItem> cachedTerrainConstructed = null;
        private static List<ScannerItem> cachedMineableRare = null;
        private static List<ScannerItem> cachedMineableStone = null;
        private static List<ScannerItem> cachedMineableScanned = null;
        private static List<ScannerItem> cachedFogItems = null;
        private static List<ScannerItem> cachedPollutedItems = null;
        private static bool fogDirty = true;
        private static int lastCellHash = 0;
        private static int lastPollutionCount = 0;

        /// <summary>
        /// Invalidates the fog portion of the cell-walk cache. Called by FogChangePatch
        /// whenever a cell's fog state changes. The next CollectMapItems will rebuild the
        /// Unexplored category — and, because fog collection shares the AllCells walk with
        /// terrain/mineables/deep ore, those caches are rebuilt at the same time.
        /// </summary>
        public static void MarkFogDirty() => fogDirty = true;

        /// <summary>
        /// Invalidates all cell-based caches. Call when the map state changes
        /// in ways not captured by StateHashOfGroup (e.g., map change, mod reload).
        /// </summary>
        public static void InvalidateCache()
        {
            cachedTerrainNatural = null;
            cachedTerrainConstructed = null;
            cachedMineableRare = null;
            cachedMineableStone = null;
            cachedMineableScanned = null;
            cachedFogItems = null;
            cachedPollutedItems = null;
            fogDirty = true;
            lastCellHash = 0;
            lastPollutionCount = 0;
            designatorLabelCache = null;
        }

        /// <summary>
        /// Gets the localized label for a DesignationDef by finding its Designator.
        /// Uses a static cache built on first call to avoid repeated reflection lookups.
        /// </summary>
        private static Dictionary<DesignationDef, string> designatorLabelCache = null;

        public static string GetLocalizedDesignationLabel(DesignationDef def)
        {
            if (def == null)
                return "RimWorldAccess.Map.Label.Unknown".Translate();

            // Build cache on first call
            if (designatorLabelCache == null)
            {
                designatorLabelCache = new Dictionary<DesignationDef, string>();
                var designators = Find.ReverseDesignatorDatabase?.AllDesignators;
                if (designators != null)
                {
                    foreach (var designator in designators)
                    {
                        var designationProp = designator.GetType().GetProperty("Designation",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public);

                        if (designationProp != null)
                        {
                            var designatorDef = designationProp.GetValue(designator) as DesignationDef;
                            if (designatorDef != null && !designatorLabelCache.ContainsKey(designatorDef))
                                designatorLabelCache[designatorDef] = designator.Label;
                        }
                    }
                }
            }

            if (designatorLabelCache.TryGetValue(def, out string label))
                return label;

            // Fallback: use LabelCap if available, otherwise format defName
            label = def.LabelCap;
            if (string.IsNullOrEmpty(label))
            {
                label = GenText.SplitCamelCase(def.defName);
            }
            return label;
        }
    }
}
