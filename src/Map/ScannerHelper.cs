using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class ScannerHelper
    {
        // Search excludes this label: every fog region carries it and would dominate results.
        // Localized property so emission and the search filter compare equal in every language.
        public static string UnexploredAreaLabel => (string)"RimWorldAccess.Map.Scanner.UnexploredArea".Translate();
        public static string PollutedAreaLabel => (string)"RimWorldAccess.Map.Scanner.PollutedArea".Translate();

        /// <summary>
        /// Adds a ScannerItem to its specialized subcategory and to the category's "All"
        /// subcategory (Subcategories[0]), which mirrors every specialized subcategory.
        /// </summary>
        private static void AddTo(ScannerCategory category, ScannerSubcategory specialized, ScannerItem item)
        {
            specialized.Items.Add(item);
            category.Subcategories[0].Items.Add(item);
        }

        /// <summary>
        /// Builds the scanner's category list from the live map.
        ///
        /// <paramref name="onlyCategory"/> restricts the build to ONE schema category: blocks that
        /// cannot feed it are skipped and the return is that category alone, or an empty list when
        /// it came back empty. "All" and "Uncategorized" are whole-map aggregates and must never be
        /// passed here — send those down the full-build path.
        /// </summary>
        public static List<ScannerCategory> CollectMapItems(Map map, IntVec3 cursorPosition, string onlyCategory = null)
        {
            var categorizedThings = new HashSet<Thing>();

            // Every schema category gets an "All" subcategory at index 0.
            var buckets = ScannerBuckets.BuildFromSchema();

            // Categories the single pass over map things can feed.
            bool wantThings = onlyCategory == null
                || onlyCategory == "Pawns" || onlyCategory == "Entities"
                || onlyCategory == "Tame" || onlyCategory == "Wild"
                || onlyCategory == "Hazards" || onlyCategory == "Buildings"
                || onlyCategory == "Trees" || onlyCategory == "Plants"
                || onlyCategory == "Items" || onlyCategory == "Mineable"
                || onlyCategory == "Orders";
            bool wantCells = onlyCategory == null
                || onlyCategory == "Terrain" || onlyCategory == "Roofs"
                || onlyCategory == "Mineable" || onlyCategory == "Unexplored";
            Func<string, bool> want = name => onlyCategory == null || onlyCategory == name;

            // One-shot lookups for the cell/designation/zone/room/plan sweeps below. The per-thing
            // dispatch loop resolves its own subcategories inside the Classify* helpers instead, so
            // a new per-thing category needs no entry here.
            var entitiesCategory = buckets.Cat("Entities");
            var entitiesCapturedSubcat = buckets.Sub("Entities-Captured");

            var terrainCategory = buckets.Cat("Terrain");
            var terrainNaturalSubcat = buckets.Sub("Terrain-Natural");
            var terrainConstructedSubcat = buckets.Sub("Terrain-Constructed");
            var terrainPollutedSubcat = buckets.Sub("Terrain-Polluted");

            var roofsCategory = buckets.Cat("Roofs");
            var roofsThickSubcat = buckets.Sub("Roofs-ThickRoof");
            var roofsThinSubcat = buckets.Sub("Roofs-ThinRoof");

            var mineableCategory = buckets.Cat("Mineable");
            var mineableRareSubcat = buckets.Sub("Mineable-Rare");
            var mineableStoneSubcat = buckets.Sub("Mineable-Stone");
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

            // Uncategorized gets per-def subcategories built on demand below.
            var uncategorizedCategory = buckets.Cat("Uncategorized");
            var uncategorizedByDef = new Dictionary<string, ScannerSubcategory>();

            var fogGrid = map.fogGrid;

            if (wantThings)
            {
                var allThings = map.listerThings.AllThings;
                var playerFaction = Faction.OfPlayer;

                foreach (var thing in allThings)
                {
                    if (!thing.Spawned || !thing.Position.IsValid)
                        continue;

                    if (fogGrid.IsFogged(thing.Position))
                        continue;

                    var item = new ScannerItem(thing, cursorPosition);

                    // The two `continue`s stay inline rather than moving into a classifier: they
                    // must also skip the uncategorized fallback below, because a hidden pawn or a
                    // natural-rock building is invisible to the scanner entirely.
                    if (thing is Pawn pawn)
                    {
                        // IsHiddenFromPlayer exempts player-faction pawns, so invisibility psycasts
                        // on colonists still surface; only hostile stealth is filtered.
                        if (pawn.IsHiddenFromPlayer())
                            continue;

                        ClassifyPawn(pawn, item, buckets, playerFaction, categorizedThings);
                    }
                    else if (thing is Fire)
                    {
                        AddTo(buckets.Cat("Hazards"), buckets.Sub("Hazards-Fire"), item);
                        categorizedThings.Add(thing);
                    }
                    else if (thing is Plant plant)
                    {
                        ClassifyPlant(plant, item, buckets, categorizedThings);
                    }
                    else if (thing is Blueprint || thing is Frame)
                    {
                        AddTo(ordersCategory, ordersConstructionSubcat, item);
                        categorizedThings.Add(thing);
                    }
                    else if (thing is Building building)
                    {
                        // Natural rock/ore is collected as mineable tiles in the cell sweep below.
                        if (building.def.building != null && building.def.building.isNaturalRock)
                            continue;

                        ClassifyBuilding(building, item, buckets, categorizedThings);
                    }
                    else if (IsStoneChunk(thing))
                    {
                        AddTo(mineableCategory, buckets.Sub("Mineable-Chunks"), item);
                        categorizedThings.Add(thing);
                    }
                    else if (!IsDebrisItem(thing))
                    {
                        ClassifyItem(thing, item, map, buckets, categorizedThings);
                    }

                    // Anything still uncategorized lands in a per-def subcategory, mirrored into
                    // Uncategorized-All.
                    if (onlyCategory == null && !categorizedThings.Contains(thing) && thing.def.selectable)
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
            }

            if (want("Entities"))
            {
                // Held pawns live in the platform's innerContainer, not in listerThings, so walk
                // platforms explicitly. AllBuildingsColonistOfClass catches HoldingPlatform and
                // HoldingSpot alike.
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
            }

            if (wantCells)
            {
                // Mineables, terrain, deep ore and fog all come out of one AllCells walk, so they
                // share a cache key and refresh together.
                int currentCellHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.BuildingArtificial);

                // Pollution (Biotech) is a per-cell overlay that changes independently of building
                // and fog state. TotalPollution is an O(1) counter and stays 0 without Biotech.
                int currentPollutionCount = ModsConfig.BiotechActive ? map.pollutionGrid.TotalPollution : 0;

                if (cachedTerrainNatural != null && currentCellHash == lastCellHash && !fogDirty
                    && currentPollutionCount == lastPollutionCount)
                {
                    RefreshRegionDistances(cachedTerrainNatural, cursorPosition);
                    RefreshRegionDistances(cachedTerrainConstructed, cursorPosition);
                    RefreshRegionDistances(cachedMineableRare, cursorPosition);
                    RefreshRegionDistances(cachedMineableStone, cursorPosition);
                    RefreshRegionDistances(cachedMineableScanned, cursorPosition);
                    RefreshRegionDistances(cachedPollutedItems, cursorPosition);
                    RefreshRegionDistances(cachedFogItems, cursorPosition);
                    RefreshRegionDistances(cachedRoofsThick, cursorPosition);
                    RefreshRegionDistances(cachedRoofsThin, cursorPosition);

                    // Reuse cached cell data, mirroring each list into the category's "All".
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

                    roofsThickSubcat.Items.AddRange(cachedRoofsThick);
                    roofsThinSubcat.Items.AddRange(cachedRoofsThin);
                    roofsCategory.Subcategories[0].Items.AddRange(cachedRoofsThick);
                    roofsCategory.Subcategories[0].Items.AddRange(cachedRoofsThin);
                }
                else
                {
                    var allCells = map.AllCells;
                    bool hasDeepScanner = map.deepResourceGrid.AnyActiveDeepScannersOnMap();
                    var deepOreByDef = new Dictionary<string, List<(IntVec3 position, int count, ThingDef oreDef)>>();

                    var mineableRareByDef = new Dictionary<string, List<(IntVec3 position, Thing thing)>>();
                    var mineableStoneByDef = new Dictionary<string, List<(IntVec3 position, Thing thing)>>();
                    var fogPositions = new List<IntVec3>();
                    var pollutedPositions = new List<IntVec3>();

                    // Natural roofs only; thick vs thin is decided per def at grouping time.
                    var roofCellsByDef = new Dictionary<RoofDef, List<IntVec3>>();

                    // Grouped into regions once here, as the mineables are: a map holds tens of
                    // thousands of terrain cells, too many to re-sort and re-group on every refresh.
                    var naturalTerrainByLabel = new Dictionary<string, List<IntVec3>>();
                    var constructedTerrainByLabel = new Dictionary<string, List<IntVec3>>();

                    foreach (var cell in allCells)
                    {
                        if (!fogGrid.IsFogged(cell))
                        {
                            var terrain = map.terrainGrid.TerrainAt(cell);

                            if (ModsConfig.BiotechActive && map.pollutionGrid.IsPolluted(cell))
                                pollutedPositions.Add(cell);

                            var edifice = cell.GetEdifice(map);
                            if (edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock)
                            {
                                string defKey = edifice.def.defName;

                                if (edifice.def.building.isResourceRock && edifice.def.building.mineableYield > 0)
                                {
                                    if (!mineableRareByDef.ContainsKey(defKey))
                                        mineableRareByDef[defKey] = new List<(IntVec3, Thing)>();
                                    mineableRareByDef[defKey].Add((cell, edifice));
                                    categorizedThings.Add(edifice);
                                }
                                else
                                {
                                    if (!mineableStoneByDef.ContainsKey(defKey))
                                        mineableStoneByDef[defKey] = new List<(IntVec3, Thing)>();
                                    mineableStoneByDef[defKey].Add((cell, edifice));
                                    categorizedThings.Add(edifice);
                                }
                            }

                            if (terrain != null)
                            {
                                // Natural terrain is anything but plain default soil, detected by
                                // property rather than defName: plain soil is fertility 1.0 with
                                // pathCost <= 2, so any other fertility or an elevated path cost
                                // marks terrain worth announcing (mud, moss, water, lava, ...).
                                if (!terrain.layerable && terrain.natural)
                                {
                                    bool isInteresting =
                                        terrain.fertility != 1.0f ||
                                        terrain.pathCost > 2;
                                    if (isInteresting)
                                    {
                                        AddCell(naturalTerrainByLabel, terrain.label, cell);
                                    }
                                }
                                else if (terrain.layerable || !terrain.natural)
                                {
                                    // Constructed floors only, never layered natural dirt.
                                    if (!terrain.natural)
                                    {
                                        AddCell(constructedTerrainByLabel, terrain.label, cell);
                                    }
                                }
                            }

                            // Fog-gated like terrain so undiscovered roofs aren't revealed.
                            var roof = map.roofGrid.RoofAt(cell);
                            if (roof != null && roof.isNatural)
                            {
                                if (!roofCellsByDef.TryGetValue(roof, out var roofList))
                                {
                                    roofList = new List<IntVec3>();
                                    roofCellsByDef[roof] = roofList;
                                }
                                roofList.Add(cell);
                            }
                        }
                        else
                        {
                            fogPositions.Add(cell);
                        }

                        // Deep ore is underground, so vanilla shows it regardless of fog.
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

                    AddTerrainRegions(naturalTerrainByLabel, terrainCategory, terrainNaturalSubcat, cursorPosition);
                    AddTerrainRegions(constructedTerrainByLabel, terrainCategory, terrainConstructedSubcat, cursorPosition);

                    foreach (var kvp in mineableRareByDef)
                    {
                        var positions = kvp.Value.Select(x => x.position).ToList();
                        var regions = GroupTerrainByAdjacency(positions, cursorPosition);
                        var primaryThing = kvp.Value[0].thing;
                        string label = primaryThing.def.label ?? (string)"RimWorldAccess.Map.Label.Unknown".Translate();

                        var item = new ScannerItem(regions, label, cursorPosition, primaryThing);
                        AddTo(mineableCategory, mineableRareSubcat, item);
                    }

                    foreach (var kvp in mineableStoneByDef)
                    {
                        var positions = kvp.Value.Select(x => x.position).ToList();
                        var regions = GroupTerrainByAdjacency(positions, cursorPosition);
                        var primaryThing = kvp.Value[0].thing;
                        string label = primaryThing.def.label ?? (string)"RimWorldAccess.Map.Label.Unknown".Translate();

                        var item = new ScannerItem(regions, label, cursorPosition, primaryThing);
                        AddTo(mineableCategory, mineableStoneSubcat, item);
                    }

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

                    // Each contiguous fog region is its own navigable item. Unexplored has only an
                    // "All" subcategory, so add directly — AddTo would double-add.
                    var fogRegions = GroupTerrainByAdjacency(fogPositions, cursorPosition);
                    foreach (var region in fogRegions)
                    {
                        var fogItem = new ScannerItem(
                            new List<TerrainRegion> { region }, UnexploredAreaLabel, cursorPosition);
                        unexploredCategory.Subcategories[0].Items.Add(fogItem);
                    }

                    // pollutedPositions is empty without Biotech.
                    foreach (var region in GroupTerrainByAdjacency(pollutedPositions, cursorPosition))
                    {
                        var pollutedItem = new ScannerItem(
                            new List<TerrainRegion> { region }, PollutedAreaLabel, cursorPosition);
                        AddTo(terrainCategory, terrainPollutedSubcat, pollutedItem);
                    }

                    // One item per RoofDef carrying all its patches as regions.
                    foreach (var kvp in roofCellsByDef)
                    {
                        var roofDef = kvp.Key;
                        var subcat = roofDef.isThickRoof ? roofsThickSubcat : roofsThinSubcat;
                        var regions = GroupTerrainByAdjacency(kvp.Value, cursorPosition);
                        var roofItem = new ScannerItem(regions, roofDef.label, cursorPosition);
                        AddTo(roofsCategory, subcat, roofItem);
                    }

                    cachedTerrainNatural = new List<ScannerItem>(terrainNaturalSubcat.Items);
                    cachedTerrainConstructed = new List<ScannerItem>(terrainConstructedSubcat.Items);
                    cachedPollutedItems = new List<ScannerItem>(terrainPollutedSubcat.Items);
                    cachedMineableRare = new List<ScannerItem>(mineableRareSubcat.Items);
                    cachedMineableStone = new List<ScannerItem>(mineableStoneSubcat.Items);
                    cachedMineableScanned = new List<ScannerItem>(mineableScannedSubcat.Items);
                    cachedRoofsThick = new List<ScannerItem>(roofsThickSubcat.Items);
                    cachedRoofsThin = new List<ScannerItem>(roofsThinSubcat.Items);
                    cachedFogItems = new List<ScannerItem>(unexploredCategory.Subcategories[0].Items);
                    fogDirty = false;

                    lastCellHash = currentCellHash;
                    lastPollutionCount = currentPollutionCount;
                }
            }

            if (want("Orders"))
            {
                var allDesignations = map.designationManager.AllDesignations;
                foreach (var designation in allDesignations)
                {
                    if (designation == null || designation.def == null)
                        continue;

                    IntVec3 targetCell = designation.target.Cell;
                    if (!targetCell.IsValid || fogGrid.IsFogged(targetCell))
                        continue;

                    if (designation.target.HasThing && !designation.target.Thing.Spawned)
                        continue;

                    var item = new ScannerItem(designation, cursorPosition);

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
                        orderSub = ordersOtherSubcat;

                    AddTo(ordersCategory, orderSub, item);
                }
            }

            if (want("Zones"))
            {
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
                    else if (zone is Zone_Fishing)
                        zoneSub = zonesFishingSubcat;
                    else
                        zoneSub = zonesOtherSubcat;

                    AddTo(zonesCategory, zoneSub, item);
                }
            }

            if (want("Rooms"))
            {
                var visibleIndoorRooms = map.regionGrid.AllRooms.Where(room =>
                    !room.PsychologicallyOutdoors &&
                    room.ProperRoom &&
                    room.Cells.Any(cell => !fogGrid.IsFogged(cell)));

                // Rooms has only an "All" subcategory, so add directly to it.
                roomsCategory.Subcategories[0].Items.AddRange(
                    visibleIndoorRooms.Select(room => new ScannerItem(room, cursorPosition)));
            }

            if (want("Plans"))
            {
                // Each Plan is one contiguous single-color region, so it becomes one clump item.
                // Color subcategories are created only for colors actually present.
                var plansCategory = buckets.Cat("Plans");
                var plansByColorSuffix = new Dictionary<string, List<ScannerItem>>();
                foreach (var plan in map.planManager.AllPlans)
                {
                    if (plan == null || plan.CellCount == 0)
                        continue;
                    var item = new ScannerItem(plan, cursorPosition);
                    plansCategory.Subcategories[0].Items.Add(item); // "All"

                    string suffix = PlanColorHelper.ColorSuffix(plan.Color);
                    if (!plansByColorSuffix.TryGetValue(suffix, out var list))
                        plansByColorSuffix[suffix] = list = new List<ScannerItem>();
                    list.Add(item);
                }
                // Game palette order first, then any modded color outside it.
                foreach (var colorDef in Designator_Plan_Add.Colors)
                {
                    string suffix = PlanColorHelper.ColorSuffix(colorDef);
                    if (plansByColorSuffix.TryGetValue(suffix, out var list))
                    {
                        var sub = new ScannerSubcategory("Plans-" + suffix);
                        sub.Items.AddRange(list);
                        buckets.RegisterDynamicSubcategory(plansCategory, sub);
                        plansByColorSuffix.Remove(suffix);
                    }
                }
                foreach (var kvp in plansByColorSuffix)
                {
                    var sub = new ScannerSubcategory("Plans-" + kvp.Key);
                    sub.Items.AddRange(kvp.Value);
                    buckets.RegisterDynamicSubcategory(plansCategory, sub);
                }
            }

            if (onlyCategory == null)
            {
                // Flatten every other category's "-All" subcategory, deduplicating by reference so
                // an item filed under two categories appears once.
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
            }

            foreach (var category in buckets.Categories)
            {
                if (onlyCategory != null && category.Name != onlyCategory)
                    continue;

                foreach (var subcat in category.Subcategories)
                {
                    subcat.Items = subcat.Items.OrderBy(i => i.Distance).ToList();
                    subcat.Items = GroupIdenticalItems(subcat.Items, cursorPosition);
                }
            }

            if (onlyCategory != null)
            {
                ScannerCategory only = buckets.Cat(onlyCategory);
                return only.IsEmpty
                    ? new List<ScannerCategory>()
                    : new List<ScannerCategory> { only };
            }

            // Drop schema categories with no items on this map.
            var finalCategories = buckets.Categories;
            finalCategories.RemoveAll(c => c.IsEmpty);

            return finalCategories;
        }

        /// <summary>
        /// Categorizes a pawn by race and faction relationship. The caller has already filtered out
        /// hidden pawns.
        /// </summary>
        private static void ClassifyPawn(Pawn pawn, ScannerItem item, ScannerBuckets buckets, Faction playerFaction, HashSet<Thing> categorizedThings)
        {
            var pawnsCategory = buckets.Cat("Pawns");

            // Anomaly entities are permanent enemies of every non-Insect faction, so a loose
            // entity is hostile by definition.
            if (pawn.RaceProps.IsAnomalyEntity)
            {
                AddTo(buckets.Cat("Entities"), buckets.Sub("Entities-Hostile"), item);
                categorizedThings.Add(pawn);
            }
            else if (pawn.RaceProps.IsMechanoid)
            {
                if (pawn.Faction == playerFaction)
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Player Mechs"), item);
                else if (pawn.HostileTo(Faction.OfPlayer))
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Hostile Mechs"), item);
                else
                    // Neutral mechs fold into Guests alongside other helpful neutrals.
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Guests"), item);
                categorizedThings.Add(pawn);
            }
            else if (pawn.RaceProps.Humanlike)
            {
                if (pawn.IsColonist)
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Colonists"), item);
                else if (pawn.IsPrisonerOfColony)
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Prisoners"), item);
                else if (pawn.IsSlaveOfColony)
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Slaves"), item);
                else if (pawn.HostileTo(Faction.OfPlayer))
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Hostile"), item);
                else
                    // Visitors, traders, quest lodgers, allied raid help, neutral factions.
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Guests"), item);
                categorizedThings.Add(pawn);
            }
            else if (pawn.RaceProps.Animal)
            {
                if (pawn.Faction == playerFaction)
                {
                    // Roamers need rope management, so they get the pen bucket.
                    var tameSub = pawn.Roamer ? buckets.Sub("Tame-Pen") : buckets.Sub("Tame-NonPen");
                    AddTo(buckets.Cat("Tame"), tameSub, item);
                }
                else
                {
                    var wildSub = pawn.HostileTo(playerFaction) ? buckets.Sub("Wild-Hostile") : buckets.Sub("Wild-Passive");
                    AddTo(buckets.Cat("Wild"), wildSub, item);
                }
                categorizedThings.Add(pawn);
            }
            else if (PlayerControllables.IsOtherControllable(pawn))
            {
                // Drafter-bearing player pawns that are neither colonists nor mechs: vehicles and
                // similar modded units.
                AddTo(pawnsCategory, buckets.Sub("Pawns-Controllable"), item);
                categorizedThings.Add(pawn);
            }
            else
            {
                // A modded race can match none of the RaceProps shapes above and carry no draft
                // controller (RimWorld of Magic golems answer false to all four). File them by
                // relationship so they don't fall through to the uncategorized bucket.
                if (pawn.Faction == playerFaction)
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Other"), item);
                else if (pawn.HostileTo(Faction.OfPlayer))
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Hostile"), item);
                else
                    AddTo(pawnsCategory, buckets.Sub("Pawns-Guests"), item);
                categorizedThings.Add(pawn);
            }
        }

        /// <summary>
        /// Categorizes a plant into Trees or Plants, split by harvestable-now. Blight adds a
        /// Hazards-Blight entry on top of that placement rather than replacing it.
        /// </summary>
        private static void ClassifyPlant(Plant plant, ScannerItem item, ScannerBuckets buckets, HashSet<Thing> categorizedThings)
        {
            if (plant.Blighted)
            {
                buckets.Sub("Hazards-Blight").Items.Add(item);
                buckets.Cat("Hazards").Subcategories[0].Items.Add(item);
            }

            bool harvestableNow = plant.HarvestableNow && plant.LifeStage == PlantLifeStage.Mature;
            if (plant.def.plant.IsTree)
            {
                var subKey = harvestableNow ? "Trees-Harvestable" : "Trees-NonHarvestable";
                AddTo(buckets.Cat("Trees"), buckets.Sub(subKey), item);
            }
            else
            {
                // Anything not yet mature (grass, immature crops) counts as debris.
                var subKey = harvestableNow ? "Plants-Harvestable" : "Plants-Debris";
                AddTo(buckets.Cat("Plants"), buckets.Sub(subKey), item);
            }

            categorizedThings.Add(plant);
        }

        /// <summary>
        /// Categorizes a non-natural-rock building: travel-related buildings first, then everything
        /// else by its def's designation category, defaulting to Structure.
        /// </summary>
        private static void ClassifyBuilding(Building building, ScannerItem item, ScannerBuckets buckets, HashSet<Thing> categorizedThings)
        {
            var buildingsCategory = buckets.Cat("Buildings");

            if (IsTravelingBuilding(building))
            {
                AddTo(buildingsCategory, buckets.Sub("Buildings-Traveling"), item);
            }
            else
            {
                var designationCategory = building.def.designationCategory;
                string subKey = "Buildings-Structure"; // default
                if (designationCategory != null)
                {
                    // Production is the only relevant category with a DesignationCategoryDefOf
                    // member; the rest have none, so they stay defName comparisons.
                    if (designationCategory == DesignationCategoryDefOf.Production)
                        subKey = "Buildings-Production";
                    else
                    {
                        switch (designationCategory.defName)
                        {
                            case "Structure": subKey = "Buildings-Structure"; break;
                            case "Furniture": subKey = "Buildings-Furniture"; break;
                            case "Power": subKey = "Buildings-Power"; break;
                            case "Security": subKey = "Buildings-Security"; break;
                            case "Misc": subKey = "Buildings-Misc"; break;
                            case "Joy": subKey = "Buildings-Recreation"; break;
                            case "Ship": subKey = "Buildings-Ship"; break;
                            case "Temperature": subKey = "Buildings-Temperature"; break;
                            default: subKey = "Buildings-Structure"; break;
                        }
                    }
                }
                AddTo(buildingsCategory, buckets.Sub(subKey), item);
            }

            categorizedThings.Add(building);
        }

        /// <summary>
        /// Categorizes a regular (non-chunk, non-debris) item by storage state: forbidden,
        /// uninstalled furniture, stored (stockpile/shelf), or scattered.
        /// </summary>
        private static void ClassifyItem(Thing thing, ScannerItem item, Map map, ScannerBuckets buckets, HashSet<Thing> categorizedThings)
        {
            var itemsCategory = buckets.Cat("Items");

            if (thing.IsForbidden(Faction.OfPlayer))
                AddTo(itemsCategory, buckets.Sub("Items-Forbidden"), item);
            else if (IsUninstalledFurniture(thing))
                AddTo(itemsCategory, buckets.Sub("Items-Furniture"), item);
            else if (IsInStorage(thing, map))
                AddTo(itemsCategory, buckets.Sub("Items-Stored"), item);
            else
                AddTo(itemsCategory, buckets.Sub("Items-Scattered"), item);

            categorizedThings.Add(thing);
        }

        private static bool IsInStorage(Thing thing, Map map)
        {
            var zone = map.zoneManager.ZoneAt(thing.Position);
            if (zone is Zone_Stockpile)
                return true;

            var storageBuilding = thing.Position.GetThingList(map)
                .OfType<Building_Storage>()
                .FirstOrDefault();

            return storageBuilding != null;
        }

        /// <summary>
        /// Checks whether a building is travel-related (transport pods, launchers, hitching spots,
        /// shuttles). A fueling port counts only while no pod is connected to it.
        /// </summary>
        private static bool IsTravelingBuilding(Building building)
        {
            if (building == null)
                return false;

            // Detect by comp, not defName, so modded variants reusing the comps are caught.
            if (building is ThingWithComps twc)
            {
                if (twc.GetComp<CompTransporter>() != null || twc.GetComp<CompLaunchable>() != null || twc.GetComp<CompShuttle>() != null)
                    return true;
            }

            string defName = building.def.defName;

            if (building.def.building != null && building.def.building.hasFuelingPort)
            {
                // A connected pod is listed instead of its port.
                IntVec3 fuelingCell = FuelingPortUtility.GetFuelingPortCell(building);
                if (fuelingCell.IsValid && building.Map != null)
                {
                    CompLaunchable launchable = FuelingPortUtility.LaunchableAt(fuelingCell, building.Map);
                    if (launchable != null)
                    {
                        return false;
                    }
                }
                return true;
            }

            if (defName.Contains("CaravanPackingSpot") || defName.Contains("HitchingSpot"))
                return true;

            return false;
        }

        private static bool IsUninstalledFurniture(Thing thing)
        {
            if (thing is MinifiedThing)
                return true;

            if (thing.def.Minifiable)
                return true;

            return false;
        }

        private static bool IsStoneChunk(Thing thing)
        {
            // Chunks is the parent of StoneChunks, so this one check covers stone, slag and
            // modded chunk defs alike.
            return thing.def.IsWithinCategory(ThingCategoryDefOf.Chunks);
        }

        private static bool IsDebrisItem(Thing thing)
        {
            // Everything else that reads as debris is already routed away: slag chunks by
            // IsStoneChunk, RubblePile by the building branch. Filth is what's left.
            return thing.def.category == ThingCategory.Filth;
        }

        /// <summary>
        /// Yields the 8 cardinal and diagonal neighbors of a cell. <see cref="Clump"/> gates each
        /// candidate against the valid-position set, so no bounds check is needed here.
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
        /// Flood-fills the contiguous 8-way region of <paramref name="validPositions"/> reachable
        /// from <paramref name="startPos"/>, via the shared <see cref="Clump.Fill{TTile}"/>.
        /// </summary>
        private static void AddCell(Dictionary<string, List<IntVec3>> byLabel, string label, IntVec3 cell)
        {
            if (!byLabel.TryGetValue(label, out var cells))
            {
                cells = new List<IntVec3>();
                byLabel[label] = cells;
            }
            cells.Add(cell);
        }

        private static void AddTerrainRegions(Dictionary<string, List<IntVec3>> byLabel, ScannerCategory category, ScannerSubcategory subcategory, IntVec3 cursorPosition)
        {
            foreach (var kvp in byLabel)
            {
                var regions = GroupTerrainByAdjacency(kvp.Value, cursorPosition);
                if (regions.Count > 0)
                    AddTo(category, subcategory, new ScannerItem(regions, kvp.Key, cursorPosition));
            }
        }

        /// <summary>Re-measures cached region items from the current cursor, as a fresh grouping would, so their order stays live.</summary>
        private static void RefreshRegionDistances(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                if (!item.HasTerrainRegions)
                    continue;
                foreach (var region in item.TerrainRegions)
                    region.Distance = (region.CenterPosition - cursorPosition).LengthHorizontal;
                item.TerrainRegions = item.TerrainRegions.OrderBy(r => r.Distance).ToList();
                item.Position = item.TerrainRegions[0].CenterPosition;
                item.Distance = item.TerrainRegions[0].Distance;
            }
        }

        internal static HashSet<IntVec3> FloodFillTerrainRegion(IntVec3 startPos, HashSet<IntVec3> validPositions)
        {
            return Clump.Fill(startPos, validPositions, EightWayNeighbors);
        }

        /// <summary>
        /// Groups same-label terrain positions into contiguous regions, sorted by distance from the
        /// cursor.
        /// </summary>
        internal static List<TerrainRegion> GroupTerrainByAdjacency(List<IntVec3> positions, IntVec3 cursorPosition)
        {
            return Clump.GroupByAdjacency(positions, EightWayNeighbors)
                .Select(set => new TerrainRegion(set.ToList(), cursorPosition))
                .OrderBy(r => r.Distance)
                .ToList();
        }

        /// <summary>
        /// Groups deep ore positions into contiguous regions with TotalQuantity populated, sorted by
        /// distance from the cursor.
        /// </summary>
        private static List<TerrainRegion> GroupDeepOreByAdjacency(
            List<(IntVec3 position, int count)> positionsWithCounts,
            IntVec3 cursorPosition)
        {
            var regions = new List<TerrainRegion>();
            var positionToCount = positionsWithCounts.ToDictionary(p => p.position, p => p.count);
            var remaining = new HashSet<IntVec3>(positionsWithCounts.Select(p => p.position));

            while (remaining.Count > 0)
            {
                var startPos = remaining.First();
                var regionPositions = FloodFillTerrainRegion(startPos, remaining);

                if (regionPositions.Count > 0)
                {
                    var regionWithCounts = regionPositions
                        .Select(pos => (pos, positionToCount[pos]))
                        .ToList();

                    var region = new TerrainRegion(regionWithCounts, cursorPosition);
                    regions.Add(region);

                    foreach (var pos in regionPositions)
                        remaining.Remove(pos);
                }
            }

            return regions.OrderBy(r => r.Distance).ToList();
        }

        /// <summary>
        /// Groups items that share a def, stuff and quality; terrain by adjacency and designations
        /// by def. Pawns, zones and rooms are unique and pass through ungrouped.
        /// </summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            var grouped = new List<ScannerItem>();

            var terrainByLabel = new Dictionary<string, List<ScannerItem>>();
            var designationsByDef = new Dictionary<DesignationDef, List<ScannerItem>>();
            var thingsByKey = new Dictionary<(ThingDef def, ThingDef stuff, QualityCategory? quality), List<ScannerItem>>();
            var passthrough = new List<ScannerItem>();

            foreach (var item in items)
            {
                // Terrain-region items arrive already grouped.
                if (item.HasTerrainRegions)
                {
                    passthrough.Add(item);
                }
                else if (item.IsTerrain)
                {
                    if (!terrainByLabel.ContainsKey(item.Label))
                        terrainByLabel[item.Label] = new List<ScannerItem>();
                    terrainByLabel[item.Label].Add(item);
                }
                else if (item.IsDesignation)
                {
                    var def = item.Designation.def;
                    if (!designationsByDef.ContainsKey(def))
                        designationsByDef[def] = new List<ScannerItem>();
                    designationsByDef[def].Add(item);
                }
                else if (item.IsZone || item.IsRoom)
                {
                    passthrough.Add(item);
                }
                else if (item.Thing is Pawn)
                {
                    passthrough.Add(item);
                }
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

            grouped.AddRange(passthrough);

            return grouped;
        }

        /// <summary>
        /// Unwraps a MinifiedThing to its inner item, or returns the thing as-is.
        /// </summary>
        private static Thing GetActualThing(Thing thing)
        {
            if (thing is MinifiedThing minified && minified.InnerThing != null)
                return minified.InnerThing;
            return thing;
        }

        /// <summary>
        /// Checks whether two things share def, stuff and quality. HP is ignored so damaged items
        /// still group with their undamaged twins.
        /// </summary>
        private static bool AreThingsIdentical(Thing a, Thing b)
        {
            var actualA = GetActualThing(a);
            var actualB = GetActualThing(b);

            if (actualA.def != actualB.def)
                return false;

            if (actualA.Stuff != actualB.Stuff)
                return false;

            var qualityA = actualA.TryGetComp<CompQuality>();
            var qualityB = actualB.TryGetComp<CompQuality>();

            if (qualityA != null && qualityB != null)
            {
                if (qualityA.Quality != qualityB.Quality)
                    return false;
            }
            else if (qualityA != null || qualityB != null)
            {
                return false;
            }

            return true;
        }

        // Cell-walk cache: terrain, mineables, deep ore, roofs, fog.
        private static List<ScannerItem> cachedTerrainNatural = null;
        private static List<ScannerItem> cachedTerrainConstructed = null;
        private static List<ScannerItem> cachedMineableRare = null;
        private static List<ScannerItem> cachedMineableStone = null;
        private static List<ScannerItem> cachedMineableScanned = null;
        private static List<ScannerItem> cachedFogItems = null;
        private static List<ScannerItem> cachedPollutedItems = null;
        private static List<ScannerItem> cachedRoofsThick = null;
        private static List<ScannerItem> cachedRoofsThin = null;
        private static bool fogDirty = true;
        private static int lastCellHash = 0;
        private static int lastPollutionCount = 0;

        /// <summary>
        /// Invalidates the fog portion of the cell-walk cache. Because fog shares the AllCells walk,
        /// the terrain, mineable and deep-ore caches rebuild with it.
        /// </summary>
        public static void MarkFogDirty() => fogDirty = true;

        /// <summary>
        /// Invalidates all cell-based caches. Call when map state changes in ways StateHashOfGroup
        /// does not capture, such as a map switch or mod reload.
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
            cachedRoofsThick = null;
            cachedRoofsThin = null;
            fogDirty = true;
            lastCellHash = 0;
            lastPollutionCount = 0;
            designatorLabelCache = null;
        }

        /// <summary>
        /// Designator labels by DesignationDef, built on first use to avoid repeated reflection.
        /// </summary>
        private static Dictionary<DesignationDef, string> designatorLabelCache = null;

        public static string GetLocalizedDesignationLabel(DesignationDef def)
        {
            if (def == null)
                return "RimWorldAccess.Map.Label.Unknown".Translate();

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

            label = def.LabelCap;
            if (string.IsNullOrEmpty(label))
            {
                label = GenText.SplitCamelCase(def.defName);
            }
            return label;
        }
    }
}
