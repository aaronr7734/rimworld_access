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
    /// Represents an item in the world scanner (settlement, quest site, caravan, etc.)
    /// </summary>
    public class WorldScannerItem
    {
        public WorldObject WorldObject { get; set; }
        public PlanetTile Tile { get; set; }
        public float Distance { get; set; }
        public string Label { get; set; }
        public string QuestName { get; set; } // For quest-related sites
        public Faction Faction { get; set; }

        public WorldScannerItem(WorldObject worldObject, PlanetTile originTile)
        {
            WorldObject = worldObject;
            Tile = worldObject.Tile;
            Faction = worldObject.Faction;

            // Calculate distance
            if (originTile.Valid && Find.WorldGrid != null)
            {
                Distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, Tile);
            }

            // Build label
            Label = worldObject.LabelShort ?? worldObject.Label ?? "Unknown";
        }

        /// <summary>
        /// Gets the compass direction from the origin tile to this item.
        /// In pole territory, returns camera-relative directions instead.
        /// </summary>
        public string GetDirectionFrom(PlanetTile fromTile)
        {
            if (!fromTile.Valid || !Tile.Valid || Find.WorldGrid == null)
                return "";

            Vector3 fromPos = Find.WorldGrid.GetTileCenter(fromTile);
            Vector3 toPos = Find.WorldGrid.GetTileCenter(Tile);
            Vector3 direction = (toPos - fromPos).normalized;

            // Check if we're in pole territory - use relative directions
            if (WorldNavigationState.IsInPoleTerritory)
            {
                return GetRelativeDirection(fromPos, toPos, direction);
            }

            // Normal compass directions
            return GetCompassDirection(fromPos, direction);
        }

        /// <summary>
        /// Gets compass direction (N/S/E/W) based on geographic coordinates.
        /// </summary>
        private string GetCompassDirection(Vector3 fromPos, Vector3 direction)
        {
            // Project onto the tangent plane at the origin point
            Vector3 up = fromPos.normalized; // "Up" is away from planet center
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            Vector3 east = Vector3.Cross(up, north).normalized;

            // Project direction onto tangent plane
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, up).normalized;

            // Calculate angle
            float dotNorth = Vector3.Dot(flatDir, north);
            float dotEast = Vector3.Dot(flatDir, east);
            double angle = Math.Atan2(dotEast, dotNorth) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            // Convert to 8-direction compass
            if (angle >= 337.5 || angle < 22.5) return "North";
            if (angle >= 22.5 && angle < 67.5) return "Northeast";
            if (angle >= 67.5 && angle < 112.5) return "East";
            if (angle >= 112.5 && angle < 157.5) return "Southeast";
            if (angle >= 157.5 && angle < 202.5) return "South";
            if (angle >= 202.5 && angle < 247.5) return "Southwest";
            if (angle >= 247.5 && angle < 292.5) return "West";
            return "Northwest";
        }

        /// <summary>
        /// Gets camera-relative direction (ahead/behind/left/right).
        /// Used near poles where compass directions are unreliable.
        /// </summary>
        private string GetRelativeDirection(Vector3 fromPos, Vector3 toPos, Vector3 direction)
        {
            if (Find.WorldCameraDriver == null)
                return "";

            // Get camera's rotation and orientation
            Quaternion cameraRotation = Find.WorldCameraDriver.sphereRotation;

            // Camera's "forward" (up on screen) and "right" (right on screen)
            Vector3 cameraForward = cameraRotation * Vector3.forward;
            Vector3 cameraRight = cameraRotation * Vector3.right;

            // Project direction onto the view plane
            Vector3 up = fromPos.normalized;
            Vector3 flatDir = Vector3.ProjectOnPlane(direction, up).normalized;
            Vector3 flatForward = Vector3.ProjectOnPlane(cameraForward, up).normalized;
            Vector3 flatRight = Vector3.ProjectOnPlane(cameraRight, up).normalized;

            // Calculate angle relative to camera's forward
            float dotForward = Vector3.Dot(flatDir, flatForward);
            float dotRight = Vector3.Dot(flatDir, flatRight);
            double angle = Math.Atan2(dotRight, dotForward) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            // Convert to 8-direction relative
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
    /// Represents a category in the world scanner.
    /// </summary>
    public class WorldScannerCategory
    {
        public string Name { get; set; }
        public List<WorldScannerItem> Items { get; set; }

        public WorldScannerCategory(string name)
        {
            Name = name;
            Items = new List<WorldScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;
    }

    /// <summary>
    /// Scanner for world map objects. Always available during world navigation.
    /// Use Page Up/Down to cycle through items, Ctrl+Page Up/Down to switch categories.
    /// </summary>
    public static class WorldScannerState
    {
        private static List<WorldScannerCategory> categories = new List<WorldScannerCategory>();
        private static int currentCategoryIndex = 0;
        private static int currentItemIndex = 0;
        private static bool autoJumpMode = false; // Default to no auto-jump

        /// <summary>
        /// Toggles auto-jump mode on/off.
        /// When enabled, camera automatically jumps to items as you navigate.
        /// </summary>
        public static void ToggleAutoJumpMode()
        {
            autoJumpMode = !autoJumpMode;
            string status = autoJumpMode ? "enabled" : "disabled";
            TolkHelper.Speak($"Auto-jump mode {status}", SpeechPriority.High);
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
            categories.Clear();

            // Category 0: Route Waypoints (only when route planner is active)
            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active && Find.WorldRoutePlanner.waypoints.Count > 0)
            {
                var waypointsCategory = new WorldScannerCategory("Route Waypoints");
                CollectWaypoints(waypointsCategory, originTile);
                if (!waypointsCategory.IsEmpty) categories.Add(waypointsCategory);
            }

            // Category 1: Quest Sites
            var questSites = new WorldScannerCategory("Quest Sites");
            CollectQuestSites(questSites, originTile);
            if (!questSites.IsEmpty) categories.Add(questSites);

            // Category 2: Player Caravans
            var caravans = new WorldScannerCategory("Caravans");
            CollectCaravans(caravans, originTile);
            if (!caravans.IsEmpty) categories.Add(caravans);

            // Category 3: Player Settlements
            var playerSettlements = new WorldScannerCategory("Player Settlements");
            CollectSettlements(playerSettlements, originTile, FactionRelationKind.Ally, playerOnly: true);
            if (!playerSettlements.IsEmpty) categories.Add(playerSettlements);

            // Category 4: Allied Settlements
            var alliedSettlements = new WorldScannerCategory("Allied Settlements");
            CollectSettlements(alliedSettlements, originTile, FactionRelationKind.Ally, playerOnly: false);
            if (!alliedSettlements.IsEmpty) categories.Add(alliedSettlements);

            // Category 5: Neutral Settlements
            var neutralSettlements = new WorldScannerCategory("Neutral Settlements");
            CollectSettlements(neutralSettlements, originTile, FactionRelationKind.Neutral, playerOnly: false);
            if (!neutralSettlements.IsEmpty) categories.Add(neutralSettlements);

            // Category 6: Hostile Settlements
            var hostileSettlements = new WorldScannerCategory("Hostile Settlements");
            CollectSettlements(hostileSettlements, originTile, FactionRelationKind.Hostile, playerOnly: false);
            if (!hostileSettlements.IsEmpty) categories.Add(hostileSettlements);

            // Category 7: Other Sites (ruins, crashed ships, etc.)
            var otherSites = new WorldScannerCategory("Other Sites");
            CollectOtherSites(otherSites, originTile);
            if (!otherSites.IsEmpty) categories.Add(otherSites);

            if (categories.Count == 0)
            {
                TolkHelper.Speak("No world objects found", SpeechPriority.High);
                return;
            }

            // Validate indices
            ValidateIndices();
        }

        /// <summary>
        /// Collects quest-related world sites.
        /// </summary>
        private static void CollectQuestSites(WorldScannerCategory category, PlanetTile originTile)
        {
            if (Find.QuestManager == null) return;

            var activeQuests = Find.QuestManager.questsInDisplayOrder
                .Where(q => q.State == QuestState.Ongoing && !q.hidden && !q.hiddenInUI)
                .ToList();

            foreach (Quest quest in activeQuests)
            {
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
                        // Try to find world object at this tile
                        worldObj = Find.WorldObjects?.ObjectsAt(tile)?.FirstOrDefault();
                    }

                    if (!tile.Valid) continue;

                    // Skip if it's a player settlement (those go in Player Settlements)
                    if (worldObj is Settlement settlement && settlement.Faction == Faction.OfPlayer)
                        continue;

                    if (worldObj != null)
                    {
                        var item = new WorldScannerItem(worldObj, originTile);
                        item.QuestName = quest.name.StripTags();
                        category.Items.Add(item);
                    }
                }
            }

            // Sort by distance
            category.Items = category.Items.OrderBy(i => i.Distance).ToList();
        }

        /// <summary>
        /// Collects player caravans.
        /// </summary>
        private static void CollectCaravans(WorldScannerCategory category, PlanetTile originTile)
        {
            var caravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();

            if (caravans == null) return;

            foreach (var caravan in caravans)
            {
                var item = new WorldScannerItem(caravan, originTile);
                category.Items.Add(item);
            }

            // Sort by distance
            category.Items = category.Items.OrderBy(i => i.Distance).ToList();
        }

        /// <summary>
        /// Collects settlements based on faction relationship.
        /// </summary>
        private static void CollectSettlements(WorldScannerCategory category, PlanetTile originTile,
            FactionRelationKind relationKind, bool playerOnly)
        {
            var settlements = Find.WorldObjects?.Settlements;
            if (settlements == null) return;

            foreach (var settlement in settlements)
            {
                if (settlement.Faction == null) continue;

                if (playerOnly)
                {
                    if (settlement.Faction != Faction.OfPlayer) continue;
                }
                else
                {
                    if (settlement.Faction == Faction.OfPlayer) continue;

                    var relation = settlement.Faction.RelationKindWith(Faction.OfPlayer);
                    if (relation != relationKind) continue;
                }

                var item = new WorldScannerItem(settlement, originTile);
                category.Items.Add(item);
            }

            // Sort by distance
            category.Items = category.Items.OrderBy(i => i.Distance).ToList();
        }

        /// <summary>
        /// Collects other world sites (not settlements, caravans, or quest sites).
        /// </summary>
        private static void CollectOtherSites(WorldScannerCategory category, PlanetTile originTile)
        {
            var allObjects = Find.WorldObjects?.AllWorldObjects;
            if (allObjects == null) return;

            // Get quest-related tiles to exclude
            var questTiles = new HashSet<int>();
            if (Find.QuestManager != null)
            {
                foreach (var quest in Find.QuestManager.questsInDisplayOrder.Where(q => q.State == QuestState.Ongoing))
                {
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
                // Skip settlements and caravans (already in their categories)
                if (worldObj is Settlement || worldObj is Caravan)
                    continue;

                // Skip quest-related sites (already in Quest Sites)
                if (questTiles.Contains(worldObj.Tile))
                    continue;

                // Skip destroyed or invalid objects
                if (!worldObj.Tile.Valid)
                    continue;

                var item = new WorldScannerItem(worldObj, originTile);
                category.Items.Add(item);
            }

            // Sort by distance
            category.Items = category.Items.OrderBy(i => i.Distance).ToList();
        }

        /// <summary>
        /// Collects route planner waypoints.
        /// Unlike other categories, waypoints are ordered by sequence, not distance.
        /// </summary>
        private static void CollectWaypoints(WorldScannerCategory category, PlanetTile originTile)
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return;

            for (int i = 0; i < planner.waypoints.Count; i++)
            {
                RoutePlannerWaypoint waypoint = planner.waypoints[i];
                if (waypoint == null || !waypoint.Tile.Valid)
                    continue;

                var item = new WorldScannerItem(waypoint, originTile);

                // Override label to show waypoint number and travel time
                StringBuilder label = new StringBuilder();
                label.Append($"Waypoint {i + 1}");

                // Add tile description
                string tileName = WorldInfoHelper.GetTileSummary(waypoint.Tile);
                if (!string.IsNullOrEmpty(tileName))
                {
                    label.Append($": {tileName}");
                }

                // Add travel time for waypoints after the first
                if (i >= 1)
                {
                    int ticksToWaypoint = planner.GetTicksToWaypoint(i);
                    string timeString = ticksToWaypoint.ToStringTicksToDays("0.#");
                    // Use clear "Estimated travel time:" instead of game's abbreviated format
                    label.Append($". Estimated travel time: {timeString}");
                }
                else
                {
                    label.Append(" (Start)");
                }

                item.Label = label.ToString();
                category.Items.Add(item);
            }

            // Don't sort by distance - keep waypoints in sequence order
        }

        private static void ValidateIndices()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                currentCategoryIndex = 0;

            var category = GetCurrentCategory();
            if (category != null && (currentItemIndex < 0 || currentItemIndex >= category.Items.Count))
                currentItemIndex = 0;
        }

        private static WorldScannerCategory GetCurrentCategory()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                return null;
            return categories[currentCategoryIndex];
        }

        private static WorldScannerItem GetCurrentItem()
        {
            var category = GetCurrentCategory();
            if (category == null) return null;
            if (currentItemIndex < 0 || currentItemIndex >= category.Items.Count)
                return null;
            return category.Items[currentItemIndex];
        }

        /// <summary>
        /// Moves to the next item in the current category.
        /// </summary>
        public static void NextItem()
        {
            if (!WorldNavigationState.IsActive) return;

            // Initialize if needed
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var category = GetCurrentCategory();
            if (category == null || category.Items.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            currentItemIndex++;
            if (currentItemIndex >= category.Items.Count)
                currentItemIndex = 0; // Wrap

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves to the previous item in the current category.
        /// </summary>
        public static void PreviousItem()
        {
            if (!WorldNavigationState.IsActive) return;

            // Initialize if needed
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var category = GetCurrentCategory();
            if (category == null || category.Items.Count == 0)
            {
                TolkHelper.Speak("No items in this category");
                return;
            }

            currentItemIndex--;
            if (currentItemIndex < 0)
                currentItemIndex = category.Items.Count - 1; // Wrap

            if (autoJumpMode)
                JumpToCurrent();
            else
                AnnounceCurrentItem();
        }

        /// <summary>
        /// Switches to the next category.
        /// </summary>
        public static void NextCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            int startIndex = currentCategoryIndex;
            do
            {
                currentCategoryIndex++;
                if (currentCategoryIndex >= categories.Count)
                    currentCategoryIndex = 0;

                if (currentCategoryIndex == startIndex) break;
            } while (GetCurrentCategory()?.IsEmpty ?? true);

            currentItemIndex = 0;
            AnnounceCurrentCategory();

            if (GetCurrentCategory()?.Items.Count > 0)
            {
                if (autoJumpMode)
                    JumpToCurrent();
                else
                    AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Switches to the previous category.
        /// </summary>
        public static void PreviousCategory()
        {
            if (!WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            int startIndex = currentCategoryIndex;
            do
            {
                currentCategoryIndex--;
                if (currentCategoryIndex < 0)
                    currentCategoryIndex = categories.Count - 1;

                if (currentCategoryIndex == startIndex) break;
            } while (GetCurrentCategory()?.IsEmpty ?? true);

            currentItemIndex = 0;
            AnnounceCurrentCategory();

            if (GetCurrentCategory()?.Items.Count > 0)
            {
                if (autoJumpMode)
                    JumpToCurrent();
                else
                    AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Jumps the camera to the current item.
        /// </summary>
        public static void JumpToCurrent()
        {
            if (!WorldNavigationState.IsActive) return;

            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            // Update world navigation state
            WorldNavigationState.CurrentSelectedTile = item.Tile;

            // Sync with game's selection system
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                if (item.WorldObject != null)
                    Find.WorldSelector.Select(item.WorldObject);
                Find.WorldSelector.SelectedTile = item.Tile;
            }

            // Jump camera and orient north-up
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(item.Tile);
                Find.WorldCameraDriver.RotateSoNorthIsUp();
            }

            // Announce with full details
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Reads distance and direction to current item from cursor position.
        /// </summary>
        public static void ReadDistanceAndDirection()
        {
            if (!WorldNavigationState.IsActive) return;

            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;
            string direction = item.GetDirectionFrom(originTile);

            // Recalculate distance from current position
            float distance = 0f;
            if (originTile.Valid && Find.WorldGrid != null)
                distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, item.Tile);

            TolkHelper.Speak($"{direction}, {distance:F0} tiles", SpeechPriority.Normal);
        }

        /// <summary>
        /// Announces the current category.
        /// </summary>
        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            int catPosition = currentCategoryIndex + 1;
            int catTotal = categories.Count;

            TolkHelper.Speak($"{category.Name}, {category.Items.Count} items. Category {catPosition} of {catTotal}", SpeechPriority.Normal);
        }

        /// <summary>
        /// Announces the current item with full details.
        /// </summary>
        private static void AnnounceCurrentItem()
        {
            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                return;
            }

            var category = GetCurrentCategory();
            PlanetTile originTile = WorldNavigationState.CurrentSelectedTile;

            // Recalculate distance from current position
            float distance = 0f;
            if (originTile.Valid && Find.WorldGrid != null)
                distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, item.Tile);

            string direction = item.GetDirectionFrom(originTile);

            // Build announcement
            List<string> parts = new List<string>();

            // Item name
            parts.Add(item.Label);

            // Quest name if applicable
            if (!string.IsNullOrEmpty(item.QuestName))
                parts.Add($"Quest: {item.QuestName}");

            // Faction info for settlements
            if (item.WorldObject is Settlement && item.Faction != null && item.Faction != Faction.OfPlayer)
                parts.Add(item.Faction.Name);

            // Direction and distance
            if (!string.IsNullOrEmpty(direction) && distance > 0.1f)
                parts.Add($"{direction}, {distance:F0} tiles");
            else if (distance <= 0.1f)
                parts.Add("Current location");

            // Position in list
            int position = currentItemIndex + 1;
            int total = category?.Items.Count ?? 0;
            parts.Add($"{position} of {total}");

            TolkHelper.Speak(string.Join(". ", parts), SpeechPriority.Normal);
        }

        /// <summary>
        /// Resets the scanner state (called when leaving world view).
        /// </summary>
        public static void Reset()
        {
            categories.Clear();
            currentCategoryIndex = 0;
            currentItemIndex = 0;
        }
    }
}
