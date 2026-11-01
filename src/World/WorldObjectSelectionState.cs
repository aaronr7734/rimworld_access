using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/lifecycle facade for world-tile object inspection (Enter on the world map at a tile
    /// with more than one inspectable object). Owns ONLY the tree DATA — eligibility filter, sort
    /// order, lazy child population, activation closures; navigation, typeahead and announcement
    /// composition live on <see cref="RimWorldAccess.Shell.WorldObjectSelectionScope"/>.
    /// </summary>
    public static class WorldObjectSelectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static PlanetTile currentTile;

        /// <summary>
        /// Stashed between <see cref="Open"/> and the scope's <c>OnPush</c> on the next dispatcher
        /// pass, which consumes it via <see cref="BuildTreeRoot"/>. A mirror push never lands
        /// same-frame as the state flip, so the multi-object list must be held in between.
        /// </summary>
        private static List<WorldObject> pendingWorldObjects;

        /// <summary>
        /// Opens world object inspection for a tile, going straight to the object when only one
        /// is inspectable.
        /// </summary>
        public static void Open(PlanetTile tile)
        {
            if (!tile.Valid || Find.WorldObjects == null)
            {
                TolkHelper.Speak("RimWorldAccess.WorldObject.NoObjectsHere".Loc());
                return;
            }

            currentTile = tile;

            var worldObjects = Find.WorldObjects.ObjectsAt(tile)
                .Where(obj => IsInspectable(obj))
                .OrderBy(obj => GetObjectSortOrder(obj))
                .ToList();

            if (worldObjects.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.WorldObject.NoObjectsToInspect".Loc());
                return;
            }

            if (worldObjects.Count == 1)
            {
                ActivateWorldObject(worldObjects[0]);
                return;
            }

            // The scope's own OnPush builds the tree and speaks the TabOpen sound, the
            // ObjectsAtTile count and the first-row announcement.
            pendingWorldObjects = worldObjects;
            IsActive = true;
        }

        /// <summary>
        /// Closes the state, from the scope's Escape router or from a tree row's Enter closure
        /// before the camera jumps.
        /// </summary>
        public static void Close()
        {
            if (!IsActive)
                return;

            IsActive = false;
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Silent session-boundary reset for StateResetRegistry: clears state with no sound and
        /// no announcement, unlike <see cref="Close"/>.
        /// </summary>
        public static void ResetHard()
        {
            IsActive = false;
            pendingWorldObjects = null;
        }

        /// <summary>
        /// Builds the tree root for the pending multi-object open. Called exactly once by
        /// <see cref="RimWorldAccess.Shell.WorldObjectSelectionScope.OnPush"/> per open.
        /// </summary>
        public static InspectionTreeItem BuildTreeRoot()
        {
            List<WorldObject> worldObjects = pendingWorldObjects ?? new List<WorldObject>();
            pendingWorldObjects = null;
            return BuildTree(worldObjects);
        }

        /// <summary>
        /// Lazy child population for an expandable object node, from the scope's
        /// <c>OnBeforeExpandNode</c>.
        /// </summary>
        public static void BuildChildrenFor(InspectionTreeItem item)
        {
            if (item.Data is WorldObject obj && item.Children.Count == 0)
            {
                BuildWorldObjectChildren(item, obj);
            }
        }

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

            // A player caravan opens its own inspect screen, as it does alone on a tile.
            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
            {
                node.IsExpandable = false;
                node.OnActivate = () =>
                {
                    Close();
                    CaravanInspectState.Open(caravan);
                };
            }

            parent.Children.Add(node);
        }

        /// <summary>
        /// Builds children for a world object when expanded (called via OnBeforeExpand).
        /// </summary>
        private static void BuildWorldObjectChildren(InspectionTreeItem objectNode, WorldObject obj)
        {
            if (objectNode.Children.Count > 0)
                return; // Already built

            if (obj is Settlement settlement)
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
                BuildGenericWorldObjectChildren(objectNode, obj);
            }
        }

        private static void BuildSettlementChildren(InspectionTreeItem parent, Settlement settlement)
        {
            int depth = parent.IndentLevel + 1;

            if (settlement.Faction == Faction.OfPlayer && settlement.HasMap)
            {
                var enterNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "RimWorldAccess.WorldObject.EnterSettlement".Translate(),
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

            if (settlement.Faction != null)
            {
                AddDetailNode(parent, depth, "RimWorldAccess.WorldObject.FactionLabel".Translate(settlement.Faction.Name));

                if (settlement.Faction != Faction.OfPlayer)
                {
                    FactionRelationKind relation = settlement.Faction.RelationKindWith(Faction.OfPlayer);
                    AddDetailNode(parent, depth, "RimWorldAccess.WorldObject.RelationLabel".Translate(relation.GetLabelCap()));
                }
            }

            if (settlement.Faction != Faction.OfPlayer &&
                settlement.Faction != null &&
                !settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                AddDetailNode(parent, depth, "RimWorldAccess.WorldObject.CanTradeHere".Translate());
            }
        }

        private static void BuildSiteChildren(InspectionTreeItem parent, Site site)
        {
            int depth = parent.IndentLevel + 1;

            string desc = site.GetDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.StripTags();
                if (desc.Length > 200)
                    desc = desc.Substring(0, 200) + "...";

                AddDetailNode(parent, depth, desc);
            }

            if (site.Faction != null)
            {
                AddDetailNode(parent, depth, "RimWorldAccess.WorldObject.FactionLabel".Translate(site.Faction.Name));
            }
        }

        private static void BuildMapParentChildren(InspectionTreeItem parent, MapParent mapParent)
        {
            int depth = parent.IndentLevel + 1;

            if (mapParent.HasMap)
            {
                var enterNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "RimWorldAccess.WorldObject.Enter".Translate(),
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

            string desc = mapParent.GetDescription();
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.StripTags();
                if (desc.Length > 200)
                    desc = desc.Substring(0, 200) + "...";

                AddDetailNode(parent, depth, desc);
            }
        }

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

        private static bool IsInspectable(WorldObject obj)
        {
            if (obj == null)
                return false;

            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
                return true;

            if (obj is Settlement settlement && settlement.Faction == Faction.OfPlayer && settlement.HasMap)
                return true;

            if (obj is Settlement otherSettlement && otherSettlement.Faction != Faction.OfPlayer)
                return true;

            if (obj is Site || obj is MapParent)
                return true;

            return false;
        }

        /// <summary>
        /// Sort rank for an object type; lower sorts first.
        /// </summary>
        private static int GetObjectSortOrder(WorldObject obj)
        {
            if (obj is Caravan c && c.Faction == Faction.OfPlayer)
                return 0;

            if (obj is Settlement s && s.Faction == Faction.OfPlayer)
                return 1;

            if (obj is Settlement)
                return 2;

            return 3;
        }

        /// <summary>
        /// A descriptive label for a world object, shared with
        /// <see cref="WorldSelectionAnnouncer"/> so keyboard and click announcements share one
        /// grammar.
        /// </summary>
        internal static string GetObjectLabel(WorldObject obj)
        {
            if (obj is Caravan caravan)
            {
                return "RimWorldAccess.WorldObject.LabelCaravan".Translate(caravan.Label);
            }

            if (obj is Settlement settlement)
            {
                if (settlement.Faction == Faction.OfPlayer)
                    return "RimWorldAccess.WorldObject.LabelYourSettlement".Translate(settlement.Label);
                else
                    return "RimWorldAccess.WorldObject.LabelWithFaction".Translate(settlement.Label, settlement.Faction?.Name ?? "RimWorldAccess.WorldObject.UnknownFactionLower".Translate().ToString());
            }

            if (obj is Site site)
            {
                return "RimWorldAccess.WorldObject.LabelSite".Translate(site.Label);
            }

            return obj.Label;
        }

        private static void ActivateWorldObject(WorldObject obj)
        {
            if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
            {
                CaravanInspectState.Open(caravan);
            }
            else if (obj is Settlement settlement && settlement.Faction == Faction.OfPlayer && settlement.HasMap)
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(settlement.Map.Center, settlement.Map));
                TolkHelper.Speak("RimWorldAccess.WorldObject.Entering".Loc(settlement.Label));
            }
            else if (obj is Settlement otherSettlement)
            {
                // Two whole-phrase keys rather than one key plus a raw relation concatenation.
                string factionName = otherSettlement.Faction?.Name ?? "RimWorldAccess.WorldObject.UnknownFaction".Translate().ToString();
                string info;
                if (otherSettlement.Faction != null)
                {
                    FactionRelationKind relation = otherSettlement.Faction.RelationKindWith(Faction.OfPlayer);
                    info = "RimWorldAccess.WorldObject.SettlementAnnounceWithRelation".Translate(otherSettlement.Label, factionName, relation.GetLabelCap()).ToString();
                }
                else
                {
                    info = "RimWorldAccess.WorldObject.SettlementAnnounce".Translate(otherSettlement.Label, factionName).ToString();
                }
                TolkHelper.SpeakData(info);
            }
            else
            {
                // A whole-phrase key rather than a raw label-plus-description concatenation.
                string desc = obj.GetDescription();
                string info = string.IsNullOrEmpty(desc)
                    ? obj.Label
                    : "RimWorldAccess.WorldObject.LabelWithDescription".Translate(obj.Label, desc.StripTags()).ToString();
                TolkHelper.SpeakData(info);
            }
        }
    }
}
