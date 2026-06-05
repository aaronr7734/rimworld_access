using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Handles inspection when world objects are present at a tile.
    /// Uses tree view pattern consistent with WindowlessInspectionState on local maps.
    /// All objects at tile are shown as root nodes with their inspection details as children.
    /// Uses TreeNavigationHelper for all standard navigation logic.
    /// </summary>
    public static class WorldObjectSelectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("WorldObjectInspect");
        private static PlanetTile currentTile;

        static WorldObjectSelectionState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnBeforeExpand = HandleBeforeExpand;
            treeNav.TrackLastChild = true;
        }

        /// <summary>
        /// Opens the world object inspection for the given tile.
        /// If only one inspectable object exists, directly opens it.
        /// </summary>
        public static void Open(PlanetTile tile)
        {
            if (!tile.Valid || Find.WorldObjects == null)
            {
                TolkHelper.Speak("RimWorldAccess.WorldObject.NoObjectsHere".Loc());
                return;
            }

            currentTile = tile;

            // Get all world objects at this tile that can be inspected
            var worldObjects = Find.WorldObjects.ObjectsAt(tile)
                .Where(obj => IsInspectable(obj))
                .OrderBy(obj => GetObjectSortOrder(obj))
                .ToList();

            if (worldObjects.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.WorldObject.NoObjectsToInspect".Loc());
                return;
            }

            // If only one object, directly open appropriate inspector
            if (worldObjects.Count == 1)
            {
                ActivateWorldObject(worldObjects[0]);
                return;
            }

            // Multiple objects - open tree view
            IsActive = true;

            var root = BuildTree(worldObjects);
            treeNav.Initialize(root);

            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Announce opening
            string announcement = "RimWorldAccess.WorldObject.ObjectsAtTile".Translate(worldObjects.Count);
            TolkHelper.SpeakData(announcement);
            treeNav.ReannounceCurrentItem();
        }

        /// <summary>
        /// Closes the world object selection state.
        /// </summary>
        public static void Close()
        {
            if (!IsActive)
                return;

            IsActive = false;
            treeNav.Reset();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Builds the tree structure for world objects.
        /// </summary>
        private static InspectionTreeItem BuildTree(List<WorldObject> worldObjects)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            foreach (var obj in worldObjects)
            {
                AddWorldObjectNode(root, obj);
            }

            return root;
        }

        /// <summary>
        /// Adds a world object node to the tree.
        /// </summary>
        private static void AddWorldObjectNode(InspectionTreeItem parent, WorldObject obj)
        {
            string label = GetObjectLabel(obj);

            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = label,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = false,
                Parent = parent,
                Data = obj
            };

            parent.Children.Add(node);
        }

        /// <summary>
        /// Builds children for a world object when expanded (called via OnBeforeExpand).
        /// </summary>
        private static void BuildWorldObjectChildren(InspectionTreeItem objectNode, WorldObject obj)
        {
            if (objectNode.Children.Count > 0)
                return; // Already built

            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
            {
                // Use CaravanInspectState's tree building for caravans
                CaravanInspectState.BuildCaravanCategoriesFor(objectNode, caravan);
            }
            else if (obj is Settlement settlement)
            {
                BuildSettlementChildren(objectNode, settlement);
            }
            else if (obj is Site site)
            {
                BuildSiteChildren(objectNode, site);
            }
            else if (obj is MapParent mapParent)
            {
                BuildMapParentChildren(objectNode, mapParent);
            }
            else
            {
                // Generic world object - show description
                BuildGenericWorldObjectChildren(objectNode, obj);
            }
        }

        /// <summary>
        /// Builds children for a settlement.
        /// </summary>
        private static void BuildSettlementChildren(InspectionTreeItem parent, Settlement settlement)
        {
            int depth = parent.IndentLevel + 1;

            // Player settlement with map - can enter
            if (settlement.Faction == Faction.OfPlayer && settlement.HasMap)
            {
                var enterNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Enter Settlement",
                    IndentLevel = depth,
                    Parent = parent,
                    Data = settlement,
                    OnActivate = () =>
                    {
                        Close();
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(settlement.Map.Center, settlement.Map));
                        TolkHelper.Speak("RimWorldAccess.WorldObject.Entering".Loc(settlement.Label));
                    }
                };
                parent.Children.Add(enterNode);
            }

            // Faction info
            if (settlement.Faction != null)
            {
                AddDetailNode(parent, depth, $"Faction: {settlement.Faction.Name}");

                // Relation if not player
                if (settlement.Faction != Faction.OfPlayer)
                {
                    FactionRelationKind relation = settlement.Faction.RelationKindWith(Faction.OfPlayer);
                    AddDetailNode(parent, depth, $"Relation: {relation}");
                }
            }

            // Trade availability for non-hostile settlements
            if (settlement.Faction != Faction.OfPlayer &&
                settlement.Faction != null &&
                !settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                AddDetailNode(parent, depth, "Can trade here");
            }
        }

        /// <summary>
        /// Builds children for a site.
        /// </summary>
        private static void BuildSiteChildren(InspectionTreeItem parent, Site site)
        {
            int depth = parent.IndentLevel + 1;

            // Site description
            string desc = site.GetDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.StripTags();
                // Truncate long descriptions
                if (desc.Length > 200)
                    desc = desc.Substring(0, 200) + "...";

                AddDetailNode(parent, depth, desc);
            }

            // Faction if present
            if (site.Faction != null)
            {
                AddDetailNode(parent, depth, $"Faction: {site.Faction.Name}");
            }
        }

        /// <summary>
        /// Builds children for a generic map parent.
        /// </summary>
        private static void BuildMapParentChildren(InspectionTreeItem parent, MapParent mapParent)
        {
            int depth = parent.IndentLevel + 1;

            // Can enter if has map
            if (mapParent.HasMap)
            {
                var enterNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Enter",
                    IndentLevel = depth,
                    Parent = parent,
                    Data = mapParent,
                    OnActivate = () =>
                    {
                        Close();
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(mapParent.Map.Center, mapParent.Map));
                        TolkHelper.Speak("RimWorldAccess.WorldObject.Entering".Loc(mapParent.Label));
                    }
                };
                parent.Children.Add(enterNode);
            }

            // Description
            string desc = mapParent.GetDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.StripTags();
                if (desc.Length > 200)
                    desc = desc.Substring(0, 200) + "...";

                AddDetailNode(parent, depth, desc);
            }
        }

        /// <summary>
        /// Builds children for a generic world object.
        /// </summary>
        private static void BuildGenericWorldObjectChildren(InspectionTreeItem parent, WorldObject obj)
        {
            int depth = parent.IndentLevel + 1;

            string desc = obj.GetDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.StripTags();
                AddDetailNode(parent, depth, desc);
            }
            else
            {
                AddDetailNode(parent, depth, obj.Label);
            }
        }

        /// <summary>
        /// Helper to add a detail text node.
        /// </summary>
        private static void AddDetailNode(InspectionTreeItem parent, int depth, string label)
        {
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = label,
                IndentLevel = depth,
                Parent = parent
            };
            parent.Children.Add(node);
        }

        /// <summary>
        /// Determines if a world object can be inspected.
        /// </summary>
        private static bool IsInspectable(WorldObject obj)
        {
            if (obj == null)
                return false;

            // Player caravans
            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
                return true;

            // Player settlements with maps (can enter)
            if (obj is Settlement settlement && settlement.Faction == Faction.OfPlayer && settlement.HasMap)
                return true;

            // Other faction settlements (for info)
            if (obj is Settlement otherSettlement && otherSettlement.Faction != Faction.OfPlayer)
                return true;

            // Sites, camps, etc.
            if (obj is Site || obj is MapParent)
                return true;

            return false;
        }

        /// <summary>
        /// Gets the sort order for object types.
        /// Lower numbers appear first.
        /// </summary>
        private static int GetObjectSortOrder(WorldObject obj)
        {
            // Player caravans first
            if (obj is Caravan c && c.Faction == Faction.OfPlayer)
                return 0;

            // Player settlements second
            if (obj is Settlement s && s.Faction == Faction.OfPlayer)
                return 1;

            // Other settlements third
            if (obj is Settlement)
                return 2;

            // Everything else
            return 3;
        }

        /// <summary>
        /// Gets a descriptive label for a world object.
        /// </summary>
        private static string GetObjectLabel(WorldObject obj)
        {
            if (obj is Caravan caravan)
            {
                return $"{caravan.Label} (caravan)";
            }

            if (obj is Settlement settlement)
            {
                if (settlement.Faction == Faction.OfPlayer)
                    return $"{settlement.Label} (your settlement)";
                else
                    return $"{settlement.Label} ({settlement.Faction?.Name ?? "unknown faction"})";
            }

            if (obj is Site site)
            {
                return $"{site.Label} (site)";
            }

            return obj.Label;
        }

        /// <summary>
        /// Activates a world object directly (when only one at tile).
        /// </summary>
        private static void ActivateWorldObject(WorldObject obj)
        {
            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
            {
                CaravanInspectState.Open(caravan);
            }
            else if (obj is Settlement settlement && settlement.Faction == Faction.OfPlayer && settlement.HasMap)
            {
                // Enter player settlement
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(settlement.Map.Center, settlement.Map));
                TolkHelper.Speak("RimWorldAccess.WorldObject.Entering".Loc(settlement.Label));
            }
            else if (obj is Settlement otherSettlement)
            {
                // For other faction settlements, announce info
                string info = $"{otherSettlement.Label}. {otherSettlement.Faction?.Name ?? "Unknown faction"}.";
                if (otherSettlement.Faction != null)
                {
                    FactionRelationKind relation = otherSettlement.Faction.RelationKindWith(Faction.OfPlayer);
                    info += $" {relation}.";
                }
                TolkHelper.SpeakData(info);
            }
            else
            {
                // For other objects, just announce their label and description
                string info = obj.Label;
                if (!string.IsNullOrEmpty(obj.GetDescription()))
                {
                    info += ". " + obj.GetDescription().StripTags();
                }
                TolkHelper.SpeakData(info);
            }
        }

        #region Announcement Formatters

        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            string label = item.Label.StripTags();

            // State indicator for expandable items
            string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

            // Position among siblings
            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);

            // Level suffix
            string levelSuffix = MenuHelper.GetLevelSuffix("WorldObjectInspect", item.IndentLevel);

            string announcement = $"{label}{stateIndicator}. {positionPart}{levelSuffix}";
            return announcement;
        }

        #endregion

        #region Custom Action Handlers

        private static bool HandleActivate(InspectionTreeItem item)
        {
            // For detail text or stat items, just read again
            if (item.Type == InspectionTreeItem.ItemType.DetailText)
            {
                TolkHelper.SpeakData(item.Label);
                return true;
            }

            // Item's own OnActivate is handled by TreeNavigationHelper
            // Categories toggle expand/collapse via default behavior
            if (item.IsExpandable)
                return false;

            if (item.OnActivate == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.WorldObject.NoActionAvailable".Loc());
                return true;
            }

            return false; // Let TreeNavigationHelper call item.OnActivate
        }

        private static void HandleBeforeExpand(InspectionTreeItem item)
        {
            // Lazily build children for world objects
            if (item.Data is WorldObject obj && item.Children.Count == 0)
            {
                BuildWorldObjectChildren(item, obj);
            }
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive)
                return false;

            Event ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                bool handled = treeNav.HandleInput(ev);
                if (handled)
                {
                    ev.Use();
                    return true;
                }

                // TreeNavigationHelper returns false for Escape with no active search
                if (key == KeyCode.Escape)
                {
                    Close();
                    TolkHelper.Speak("RimWorldAccess.WorldObject.SelectionClosed".Loc());
                    ev.Use();
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
