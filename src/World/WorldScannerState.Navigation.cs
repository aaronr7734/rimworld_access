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
        #region Navigation

        private static void SortItemsByDistance(List<WorldScannerItem> items, PlanetTile originTile)
        {
            // By nearest tile, not center, so a patch you stand on sorts first. OrderBy is
            // stable, so equal distances keep their input order and ties never ping-pong.
            var sorted = items.OrderBy(i => i.NearestDistance(originTile)).ToList();
            items.Clear();
            items.AddRange(sorted);

            // Instances sort against the same origin, so within-item navigation and the announced
            // "Region N of M" follow live proximity too.
            foreach (var item in items)
                item.SortInstancesByDistance(originTile);
        }

        /// <summary>Re-sorts the current subcategory by distance from the origin tile.</summary>
        private static void RecalculateCurrentDistances()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;
            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            SortItemsByDistance(subcat.Items, originTile);
        }

        /// <summary>Drops every scanner cache; call when world state changes externally.</summary>
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

        // The ExtraIndex-vs-InstanceCount clamp is the one step the map scanner does not need.
        private static void ValidateIndices() => worldCursor.ValidateIndices(item => item.InstanceCount);

        private static void SkipEmptySubcategories(bool forward) => worldCursor.SkipEmptySubcategories(forward);

        private static WorldScannerCategory GetCurrentCategory() => worldCursor.GetCurrentCategory();

        private static WorldScannerSubcategory GetCurrentSubcategory() => worldCursor.GetCurrentSubcategory();

        private static WorldScannerItem GetCurrentItem() => worldCursor.GetCurrentItem();

        /// <summary>Ctrl+PageDown.</summary>
        public static void NextCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (worldCursor.Categories.Count == 0) return;

            // Changing category ends the navigation session.
            InvalidateNavigationSession();

            worldCursor.CategoryIndex++;
            if (worldCursor.CategoryIndex >= worldCursor.Categories.Count)
            {
                worldCursor.CategoryIndex = 0;
                MenuHelper.PlayWrapTone();
            }

            worldCursor.SubcategoryIndex = 0;
            worldCursor.ItemIndex = 0;
            worldCursor.ExtraIndex = 0;
            SkipEmptySubcategories(forward: true);
            RecalculateCurrentDistances();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        /// <summary>Ctrl+PageUp.</summary>
        public static void PreviousCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (worldCursor.Categories.Count == 0) return;

            // Changing category ends the navigation session.
            InvalidateNavigationSession();

            worldCursor.CategoryIndex--;
            if (worldCursor.CategoryIndex < 0)
            {
                worldCursor.CategoryIndex = worldCursor.Categories.Count - 1;
                MenuHelper.PlayWrapTone();
            }

            worldCursor.SubcategoryIndex = 0;
            worldCursor.ItemIndex = 0;
            worldCursor.ExtraIndex = 0;
            SkipEmptySubcategories(forward: true);
            RecalculateCurrentDistances();

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        /// <summary>Shift+PageDown.</summary>
        public static void NextSubcategory()
        {
            if (!WorldNavigationState.IsActive) return;

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var category = GetCurrentCategory();
            if (category == null || category.Subcategories.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoSubcategories".Loc(), SpeechPriority.Normal);
                return;
            }

            // Changing subcategory ends the navigation session.
            InvalidateNavigationSession();

            int startIndex = worldCursor.SubcategoryIndex;
            do
            {
                worldCursor.SubcategoryIndex++;
                if (worldCursor.SubcategoryIndex >= category.Subcategories.Count)
                {
                    worldCursor.SubcategoryIndex = 0;
                    MenuHelper.PlayWrapTone();
                }
                if (worldCursor.SubcategoryIndex == startIndex) break;
            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            worldCursor.ItemIndex = 0;
            worldCursor.ExtraIndex = 0;
            RecalculateCurrentDistances();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        /// <summary>Shift+PageUp.</summary>
        public static void PreviousSubcategory()
        {
            if (!WorldNavigationState.IsActive) return;

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var category = GetCurrentCategory();
            if (category == null || category.Subcategories.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoSubcategories".Loc(), SpeechPriority.Normal);
                return;
            }

            // Changing subcategory ends the navigation session.
            InvalidateNavigationSession();

            int startIndex = worldCursor.SubcategoryIndex;
            do
            {
                worldCursor.SubcategoryIndex--;
                if (worldCursor.SubcategoryIndex < 0)
                {
                    worldCursor.SubcategoryIndex = category.Subcategories.Count - 1;
                    MenuHelper.PlayWrapTone();
                }
                if (worldCursor.SubcategoryIndex == startIndex) break;
            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            worldCursor.ItemIndex = 0;
            worldCursor.ExtraIndex = 0;
            RecalculateCurrentDistances();

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        /// <summary>PageDown.</summary>
        public static void NextItem()
        {
            if (!WorldNavigationState.IsActive) return;

            // Always refresh, to catch world object changes: cheap on a cache hit, and a miss
            // invalidates the session so the next press re-sorts against fresh data.
            bool wasEmpty = worldCursor.Categories.Count == 0;
            RefreshItems();
            if (worldCursor.Categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoItemsInCategory".Loc(), SpeechPriority.Normal);
                return;
            }

            AdvanceInSession(forward: true, subcat);
        }

        /// <summary>PageUp.</summary>
        public static void PreviousItem()
        {
            if (!WorldNavigationState.IsActive) return;

            bool wasEmpty = worldCursor.Categories.Count == 0;
            RefreshItems();
            if (worldCursor.Categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoItemsInCategory".Loc(), SpeechPriority.Normal);
                return;
            }

            AdvanceInSession(forward: false, subcat);
        }

        /// <summary>
        /// Shared step for NextItem/PreviousItem: starts a session on the first press, then walks
        /// the frozen sort order. If the origin tile drifted from where the last jump left it, the
        /// session is invalidated first so a fresh one begins.
        /// </summary>
        private static void AdvanceInSession(bool forward, WorldScannerSubcategory subcat)
        {
            if (navSession.HasDrifted(WorldNavigationState.CurrentSelectedTile))
            {
                InvalidateNavigationSession();
            }

            if (!navSession.Active)
            {
                BeginNavigationSession();
            }

            if (forward)
            {
                worldCursor.ItemIndex++;
                if (worldCursor.ItemIndex >= subcat.Items.Count)
                {
                    worldCursor.ItemIndex = 0;
                    if (subcat.Items.Count > 1) MenuHelper.PlayWrapTone();
                }
            }
            else
            {
                worldCursor.ItemIndex--;
                if (worldCursor.ItemIndex < 0)
                {
                    worldCursor.ItemIndex = subcat.Items.Count - 1;
                    if (subcat.Items.Count > 1) MenuHelper.PlayWrapTone();
                }
            }

            worldCursor.ExtraIndex = 0;

            if (autoJumpMode)
            {
                // Auto-jump skips AnnounceCurrentItem, which is what picks the nearest region.
                worldCursor.ExtraIndex = NearestInstanceIndex(GetCurrentItem(), WorldNavigationState.CurrentSelectedTile);
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }

            navSession.RecordPosition(WorldNavigationState.CurrentSelectedTile);
        }

        /// <summary>Alt+PageDown: the next instance of the current item type.</summary>
        public static void NextInstance()
        {
            if (!WorldNavigationState.IsActive) return;

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null || !item.HasInstances)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoInstancesToNavigate".Loc(), SpeechPriority.Normal);
                return;
            }

            worldCursor.ExtraIndex++;
            if (worldCursor.ExtraIndex >= item.InstanceCount)
            {
                worldCursor.ExtraIndex = 0;
                if (item.InstanceCount > 1) MenuHelper.PlayWrapTone();
            }

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentInstance();
        }

        /// <summary>Alt+PageUp: the previous instance of the current item type.</summary>
        public static void PreviousInstance()
        {
            if (!WorldNavigationState.IsActive) return;

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (item == null || !item.HasInstances)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoInstancesToNavigate".Loc(), SpeechPriority.Normal);
                return;
            }

            worldCursor.ExtraIndex--;
            if (worldCursor.ExtraIndex < 0)
            {
                worldCursor.ExtraIndex = item.InstanceCount - 1;
                if (item.InstanceCount > 1) MenuHelper.PlayWrapTone();
            }

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentInstance();
        }

        /// <summary>Home: jumps the camera to the current item or instance.</summary>
        public static void JumpToCurrent(bool manual = false)
        {
            if (!WorldNavigationState.IsActive) return;

            // Collapse a same-frame double Home arriving from both world keyboard routes.
            if (manual)
            {
                int frame = Time.frameCount;
                if (frame == lastManualJumpFrame) return;
                lastManualJumpFrame = frame;
            }

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (!GuardHelper.RequireItem(item, SpeechPriority.High)) return;

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            bool jumpedToCenter = false;

            // For region clumps: from off the clump land on the nearest edge tile, a manual Home
            // from on it jumps to the center, auto-jump always takes the nearest. During launch
            // targeting only reachable tiles are candidates.
            PlanetTile targetTile;
            PlanetTile clumpCenter = PlanetTile.Invalid;
            int[] members = FeatureMemberTiles(item, worldCursor.ExtraIndex);
            if (members != null && members.Length > 0)
            {
                PlanetTile centerTile = item.GetTileAtInstance(worldCursor.ExtraIndex);
                clumpCenter = centerTile;
                var plan = ClumpNav.PlanHome(members, centerTile.tileId, originTile.tileId, WorldTileMetric, manual);
                targetTile = new PlanetTile(plan.Tile, -1);
                jumpedToCenter = plan.IsCenter;
            }
            else
            {
                targetTile = item.GetTileAtInstance(worldCursor.ExtraIndex);
            }

            // Landing on the occupied tile confirms position instead of re-announcing it, naming
            // the patch center distinctly from any other tile.
            if (targetTile.Valid && targetTile == originTile)
            {
                bool atCenter = members != null && members.Length > 1
                    && clumpCenter.Valid && originTile == clumpCenter;
                string where = atCenter
                    ? (string)"RimWorldAccess.WorldScanner.AlreadyAtCenter".Translate(item.Label)
                    : (string)"RimWorldAccess.WorldScanner.AlreadyAt".Translate(item.Label);
                TolkHelper.SpeakData(where, SpeechPriority.Normal);
                return;
            }

            // Follow the item onto its own planet layer.
            if (targetTile.Valid && targetTile.Layer != PlanetLayer.Selected)
            {
                PlanetLayer.Selected = targetTile.Layer;
                TolkHelper.Speak("RimWorldAccess.WorldScanner.SwitchedToLayer".Loc(targetTile.LayerDef.LabelCap));
            }

            // Guard the write so NotifyOriginWritten does not invalidate the session: this move
            // is scanner-driven, not an external cursor change.
            navSession.ScannerDrivenJumpInProgress = true;
            try
            {
                WorldNavigationState.CurrentSelectedTile = targetTile;
            }
            finally
            {
                navSession.ScannerDrivenJumpInProgress = false;
            }

            WorldNavigationState.SyncSelectionWithGame();
            if (item.WorldObject != null && Find.WorldSelector != null)
                Find.WorldSelector.Select(item.WorldObject);

            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(targetTile);
                Find.WorldCameraDriver.RotateSoNorthIsUp();
            }

            if (jumpedToCenter)
            {
                // Lead with the move delta, then the destination tile in the same utterance: the
                // world has no terrain sound, so speech is the only arrival confirmation.
                float dist = Find.WorldGrid.ApproxDistanceInTiles(originTile, targetTile);
                string dir = WorldScannerItem.GetDirectionFromTile(originTile, targetTile);
                string prefix = !string.IsNullOrEmpty(dir)
                    ? "RimWorldAccess.WorldScanner.JumpedTilesToCenter".Translate(dist.ToString("F0"), dir).ToString()
                    : "RimWorldAccess.WorldScanner.JumpedToCenter".Translate().ToString();
                WorldNavigationState.AnnounceTile(prefix);
            }
            else
            {
                // Announce as if arrowed onto: full tile info, which also updates
                // BiomeDescriptionTracker.
                WorldNavigationState.AnnounceTile();
            }
        }

        /// <summary>End: reads distance and direction to the current item.</summary>
        public static void ReadDistanceAndDirection()
        {
            if (!WorldNavigationState.IsActive) return;

            if (worldCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (worldCursor.Categories.Count == 0) return;
            }

            var item = GetCurrentItem();
            if (!GuardHelper.RequireItem(item, SpeechPriority.High)) return;

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

            // Distance and direction both point at this instance's nearest reachable tile.
            PlanetTile target = item.GetTileAtInstance(worldCursor.ExtraIndex);
            int[] members = FeatureMemberTiles(item, worldCursor.ExtraIndex);
            if (members != null && members.Length > 0 && originTile.Valid)
                target = new PlanetTile(ClumpNav.NearestMember(members, originTile.tileId, WorldTileMetric, out _), -1);

            float distance = item.GetDistance(originTile, worldCursor.ExtraIndex);
            string direction = WorldScannerItem.GetDirectionFromTile(originTile, target);

            TolkHelper.Speak("RimWorldAccess.WorldScanner.DirectionDistance".Loc(direction, distance.ToString("F0")), SpeechPriority.Normal);
        }

        #endregion

        #region Announcements

        // Heading with its instance count; every caller follows with AnnounceCurrentItem.
        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            TolkHelper.Speak("RimWorldAccess.WorldScanner.CategoryAnnouncement".Loc(category.Name, category.InstanceCount), SpeechPriority.Normal);
        }

        private static void AnnounceCurrentSubcategory()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;

            TolkHelper.Speak("RimWorldAccess.WorldScanner.SubcategoryAnnouncement".Loc(subcat.Name, subcat.InstanceCount), SpeechPriority.Normal);
        }

        private static void AnnounceCurrentItem()
        {
            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("RimWorldAccess.WorldScanner.NoItemsInCategory".Loc(), SpeechPriority.Normal);
                return;
            }

            var subcat = GetCurrentSubcategory();
            var category = GetCurrentCategory();

            bool onDifferentLayer = IsOnDifferentLayer(item);

            var parts = new List<string>();

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

            // Describe the region nearest the origin and anchor instance navigation there.
            worldCursor.ExtraIndex = NearestInstanceIndex(item, originTile);

            float distance = 0f;
            string direction = "";
            bool distanceCapped = false;
            // The tile distance, direction, and fuel cost all point at: the instance center, or
            // the nearest reachable edge tile for a same-layer region.
            PlanetTile announceTile = item.GetTileAtInstance(worldCursor.ExtraIndex);

            if (onDifferentLayer)
            {
                // Project the origin onto the target layer for a comparable distance.
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
                // Measure to the region's nearest edge tile, not its center, so standing on the
                // patch reads as the current location. During a launch only reachable tiles count.
                PlanetTile centerTile = item.GetTileAtInstance(worldCursor.ExtraIndex);
                int[] members = FeatureMemberTiles(item, worldCursor.ExtraIndex);
                if (members != null && members.Length > 0 && originTile.Valid)
                {
                    int nearestId = ClumpNav.NearestMember(members, originTile.tileId, WorldTileMetric, out _);
                    announceTile = new PlanetTile(nearestId, -1);
                }

                direction = WorldScannerItem.GetDirectionFromTile(originTile, announceTile);

                // Capped and memoized traversal distance, so rapid Page Up/Down stays responsive
                // on big worlds. Fuel and launch range are validated separately, uncapped.
                if (originTile.Valid && announceTile.Valid)
                    distance = GetAnnouncementTraversalDistance(originTile, announceTile, out distanceCapped);

                // Reachability from the last waypoint, measured against the instance tile.
                if (centerTile.Valid && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active &&
                    Find.WorldRoutePlanner.waypoints.Count > 0 && Find.WorldReachability != null)
                {
                    var lastWaypoint = Find.WorldRoutePlanner.waypoints[Find.WorldRoutePlanner.waypoints.Count - 1];
                    if (lastWaypoint != null && lastWaypoint.Tile.Valid)
                    {
                        if (!Find.WorldReachability.CanReach(lastWaypoint.Tile, centerTile))
                        {
                            parts.Add("RimWorldAccess.WorldScanner.Unreachable".Translate());
                        }
                    }
                }
            }

            parts.Add(item.Label);

            if (!string.IsNullOrEmpty(item.QuestName))
                parts.Add("RimWorldAccess.WorldScanner.QuestPrefix".Translate(item.QuestName));

            // Settlements add faction type and goodwill.
            if (item.WorldObject is Settlement settlement && item.Faction != null && item.Faction != Faction.OfPlayer)
            {
                string factionType = item.Faction.def?.LabelCap ?? "";
                if (!string.IsNullOrEmpty(factionType) && factionType != item.Faction.Name)
                    parts.Add("RimWorldAccess.WorldScanner.FactionWithType".Translate(item.Faction.Name, factionType));
                else
                    parts.Add(item.Faction.Name);

                string relationship = item.Faction.HostileTo(Faction.OfPlayer) ? "RimWorldAccess.World.Settlement.HostileRelationship".Translate().ToString() :
                                     item.Faction.PlayerRelationKind.GetLabelCap();
                int goodwill = item.Faction.PlayerGoodwill;
                string goodwillStr = goodwill >= 0
                    ? (string)"RimWorldAccess.World.Settlement.GoodwillPositive".Translate(goodwill)
                    : goodwill.ToString();
                parts.Add("RimWorldAccess.WorldScanner.RelationshipGoodwill".Translate(relationship, goodwillStr));

            }

            // Biomes and roads name the nearest region and its position.
            if (item.HasInstances)
            {
                if (item.BiomeRegions != null)
                    parts.Add($"{item.BiomeRegions[worldCursor.ExtraIndex].SizeDescription}");
                else if (item.RoadSegments != null)
                    parts.Add($"{item.RoadSegments[worldCursor.ExtraIndex].SizeDescription}");

                // Live proximity rank, not the build-time list slot: the nearest patch is always
                // "Region 1 of N".
                parts.Add("RimWorldAccess.WorldScanner.RegionPosition".Translate(item.InstanceRank(originTile, worldCursor.ExtraIndex), item.InstanceCount).ToString());
            }

            if (!string.IsNullOrEmpty(direction) && distance > 0.1f)
            {
                if (distanceCapped)
                    parts.Add("RimWorldAccess.WorldScanner.DirectionOverTiles".Translate(direction, ANNOUNCE_TRAVERSAL_CAP.ToString("F0")).ToString());
                else
                    parts.Add("RimWorldAccess.WorldScanner.DirectionDistanceTiles".Translate(direction, distance.ToString("F0")).ToString());
            }
            else if (distance <= 0.1f && !onDifferentLayer)
            {
                parts.Add("RimWorldAccess.WorldScanner.CurrentLocation".Translate());
            }

            // Cross-layer items name their layer.
            if (onDifferentLayer)
            {
                var itemTile = item.GetTileAtInstance(0);
                if (itemTile.Valid)
                {
                    string layerName = itemTile.LayerDef.LabelCap;
                    parts.Add("RimWorldAccess.WorldScanner.OnLayer".Translate(layerName));
                }
            }

            // Fuel cost is quoted for the same tile the distance points at, so both describe one
            // landing tile.
            if (TransportPodLaunchState.ShouldAnnounceFuelCosts() && distance > 0.1f)
            {
                // Tile-based, to match the game's own range check.
                string fuelInfo = announceTile.Valid
                    ? TransportPodLaunchState.GetFuelCostAnnouncementForTile(announceTile)
                    : TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }
            else if (GravshipDestinationState.ShouldAnnounceFuelCosts())
            {
                if (announceTile.Valid)
                {
                    string fuelInfo = GravshipDestinationState.GetFuelCostAnnouncement(announceTile);
                    if (!string.IsNullOrEmpty(fuelInfo))
                        parts.Add(fuelInfo);
                }
            }

            AddExternalTargetingInfo(parts, announceTile);

            // Standing on a multi-tile patch with a distinct center offers the second Home press;
            // during a launch the center offered is a reachable one.
            int[] hintMembers = FeatureMemberTiles(item, worldCursor.ExtraIndex);
            if (hintMembers != null &&
                ClumpNav.OffersCenter(hintMembers, item.GetTileAtInstance(worldCursor.ExtraIndex).tileId, originTile.tileId))
                parts.Add("RimWorldAccess.Map.Scanner.PressHomeForCenter".Translate());

            int pos = worldCursor.ItemIndex + 1;
            int total = subcat?.Items.Count ?? 0;
            parts.Add("RimWorldAccess.WorldScanner.PositionOf".Translate(pos, total));

            TolkHelper.SpeakData(string.Join(". ", parts), SpeechPriority.Normal);
        }

        private static void AddExternalTargetingInfo(List<string> parts, PlanetTile tile)
        {
            if (!tile.Valid)
                return;
            string info = ExternalWorldTargeting.ActiveProvider?.DestinationInfo(tile);
            if (!string.IsNullOrEmpty(info))
                parts.Add(info);
        }

        private static void AnnounceCurrentInstance()
        {
            var item = GetCurrentItem();
            if (item == null || !item.HasInstances) return;

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

            // Measure to this instance's nearest edge tile; during a launch, a reachable one.
            PlanetTile centerTile = item.GetTileAtInstance(worldCursor.ExtraIndex);
            PlanetTile announceTile = centerTile;
            int[] members = FeatureMemberTiles(item, worldCursor.ExtraIndex);
            if (members != null && members.Length > 0 && originTile.Valid)
            {
                int nearestId = ClumpNav.NearestMember(members, originTile.tileId, WorldTileMetric, out _);
                announceTile = new PlanetTile(nearestId, -1);
            }

            string direction = WorldScannerItem.GetDirectionFromTile(originTile, announceTile);

            // Capped and memoized, for responsive instance navigation.
            bool distanceCapped = false;
            float distance = (originTile.Valid && announceTile.Valid)
                ? GetAnnouncementTraversalDistance(originTile, announceTile, out distanceCapped)
                : 0f;

            var parts = new List<string>();

            // Reachability from the last waypoint, measured against the instance tile.
            if (centerTile.Valid && Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active &&
                Find.WorldRoutePlanner.waypoints.Count > 0 && Find.WorldReachability != null)
            {
                var lastWaypoint = Find.WorldRoutePlanner.waypoints[Find.WorldRoutePlanner.waypoints.Count - 1];
                if (lastWaypoint != null && lastWaypoint.Tile.Valid)
                {
                    if (!Find.WorldReachability.CanReach(lastWaypoint.Tile, centerTile))
                    {
                        parts.Add("RimWorldAccess.WorldScanner.Unreachable".Translate());
                    }
                }
            }

            parts.Add(item.Label);

            if (item.BiomeRegions != null && worldCursor.ExtraIndex < item.BiomeRegions.Count)
            {
                var region = item.BiomeRegions[worldCursor.ExtraIndex];
                parts.Add(region.SizeDescription);
            }
            else if (item.RoadSegments != null && worldCursor.ExtraIndex < item.RoadSegments.Count)
            {
                var segment = item.RoadSegments[worldCursor.ExtraIndex];
                parts.Add(segment.SizeDescription);
            }

            if (!string.IsNullOrEmpty(direction) && distance > 0.1f)
            {
                if (distanceCapped)
                    parts.Add("RimWorldAccess.WorldScanner.DirectionOverTiles".Translate(direction, ANNOUNCE_TRAVERSAL_CAP.ToString("F0")).ToString());
                else
                    parts.Add("RimWorldAccess.WorldScanner.DirectionDistanceTiles".Translate(direction, distance.ToString("F0")).ToString());
            }
            else if (distance <= 0.1f)
                parts.Add("RimWorldAccess.WorldScanner.CurrentLocation".Translate());

            // Fuel cost while launch targeting is active.
            if (TransportPodLaunchState.ShouldAnnounceFuelCosts() && distance > 0.1f)
            {
                string fuelInfo = TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }
            else if (GravshipDestinationState.ShouldAnnounceFuelCosts() && announceTile.Valid)
            {
                string fuelInfo = GravshipDestinationState.GetFuelCostAnnouncement(announceTile);
                if (!string.IsNullOrEmpty(fuelInfo))
                    parts.Add(fuelInfo);
            }

            AddExternalTargetingInfo(parts, announceTile);

            // Standing on this instance's patch with a distinct center offers the second Home.
            if (members != null &&
                ClumpNav.OffersCenter(members, centerTile.tileId, originTile.tileId))
                parts.Add("RimWorldAccess.Map.Scanner.PressHomeForCenter".Translate());

            // Live proximity rank, not the build-time list slot.
            int pos = item.InstanceRank(originTile, worldCursor.ExtraIndex);
            int total = item.InstanceCount;
            parts.Add("RimWorldAccess.WorldScanner.RegionOf".Translate(pos, total));

            TolkHelper.SpeakData(string.Join(". ", parts), SpeechPriority.Normal);
        }

        #endregion

        /// <summary>Resets the scanner state.</summary>
        public static void Reset()
        {
            worldCursor.Categories.Clear();
            worldCursor.CategoryIndex = 0;
            worldCursor.SubcategoryIndex = 0;
            worldCursor.ItemIndex = 0;
            worldCursor.ExtraIndex = 0;
            InvalidateCache();
            worldCursor.ClearTemporaryCategoryReference();
            worldCursor.ClearSavedFocus();

            InvalidateNavigationSession();
            ScannerSearchState.ClearSearchSilent();
        }
    }
}
