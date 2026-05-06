using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper functions for shelf linking feature.
    /// Provides storage discovery, compatibility checks, and linking operations.
    /// </summary>
    public static class ShelfLinkingHelper
    {
        /// <summary>
        /// Gets all compatible storage members in a room.
        /// Compatible means they have the same storageGroupTag as the source.
        /// </summary>
        /// <param name="room">The room to search</param>
        /// <param name="storageGroupTag">The tag to match (from source storage)</param>
        /// <param name="map">The current map</param>
        /// <param name="excludeSource">Optional source to exclude from results</param>
        /// <returns>List of compatible storage members in the room</returns>
        public static List<IStorageGroupMember> GetCompatibleStorageInRoom(
            Room room, string storageGroupTag, Map map, IStorageGroupMember excludeSource = null)
        {
            var results = new List<IStorageGroupMember>();

            if (room == null || map == null)
                return results;

            // Iterate through all cells in the room
            foreach (var cell in room.Cells)
            {
                var storage = GetStorageAt(cell, storageGroupTag, map);
                if (storage != null && storage != excludeSource && !results.Contains(storage))
                {
                    results.Add(storage);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets the storage member at a specific position with matching tag.
        /// </summary>
        /// <param name="pos">The position to check</param>
        /// <param name="tag">The storageGroupTag to match</param>
        /// <param name="map">The current map</param>
        /// <returns>The storage member if found and compatible, null otherwise</returns>
        public static IStorageGroupMember GetStorageAt(IntVec3 pos, string tag, Map map)
        {
            if (!pos.InBounds(map))
                return null;

            // Check all things at position for IStorageGroupMember
            var things = pos.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing is IStorageGroupMember member)
                {
                    // Check if the storage group tag matches
                    if (member.StorageGroupTag == tag)
                    {
                        return member;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all storage members at a position (regardless of tag) for cursor feedback.
        /// </summary>
        /// <param name="pos">The position to check</param>
        /// <param name="map">The current map</param>
        /// <returns>List of all storage members at position</returns>
        public static List<IStorageGroupMember> GetAllStorageAt(IntVec3 pos, Map map)
        {
            var results = new List<IStorageGroupMember>();

            if (!pos.InBounds(map))
                return results;

            var things = pos.GetThingList(map);
            foreach (var thing in things)
            {
                if (thing is IStorageGroupMember member)
                {
                    results.Add(member);
                }
            }

            return results;
        }

        /// <summary>
        /// Formats a count of storage items for announcement.
        /// Groups by type (e.g., "3 shelves, 1 bookcase").
        /// </summary>
        /// <param name="items">List of storage members to format</param>
        /// <returns>Formatted string like "3 shelves, 1 bookcase"</returns>
        public static string FormatStorageCount(List<IStorageGroupMember> items)
        {
            if (items == null || items.Count == 0)
                return "RimWorldAccess.Building.Shelf.NoStorage".Translate();

            // Group by def name
            var groups = items
                .Where(i => i is Thing)
                .Cast<Thing>()
                .GroupBy(t => t.def)
                .Select(g => new { Def = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            if (groups.Count == 0)
                return "RimWorldAccess.Building.Shelf.StorageItemsCount".Translate(items.Count);

            var parts = new List<string>();
            foreach (var group in groups)
            {
                string label = group.Count > 1
                    ? Find.ActiveLanguageWorker.Pluralize(group.Def.label, group.Count)
                    : group.Def.label;
                parts.Add("RimWorldAccess.Building.Shelf.StorageItemEntry".Translate(group.Count, label));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Gets items that are already linked to a different storage group.
        /// </summary>
        /// <param name="items">Items to check</param>
        /// <param name="excludeGroup">The source's group to exclude (items in same group are OK)</param>
        /// <returns>List of items already in a different group</returns>
        public static List<IStorageGroupMember> GetAlreadyLinkedItems(
            List<IStorageGroupMember> items, StorageGroup excludeGroup)
        {
            return items
                .Where(i => i.Group != null && i.Group != excludeGroup)
                .ToList();
        }

        /// <summary>
        /// Links all storage items to the source's group (or creates a new group if source has none).
        /// Uses the game's SetStorageGroup extension method.
        /// </summary>
        /// <param name="sourceStorage">The source storage member (determines group settings)</param>
        /// <param name="itemsToLink">Items to link to the source's group</param>
        /// <param name="map">The current map</param>
        /// <returns>True if linking succeeded, false otherwise</returns>
        public static bool LinkStorageItems(IStorageGroupMember sourceStorage, List<IStorageGroupMember> itemsToLink, Map map)
        {
            if (sourceStorage == null || itemsToLink == null || itemsToLink.Count == 0 || map == null)
                return false;

            // Get or create the storage group (same logic as game's linking gizmo)
            StorageGroup group = sourceStorage.Group;
            if (group == null)
            {
                // Create a new group and initialize from source
                group = map.storageGroups.NewGroup();
                group.InitFrom(sourceStorage);
                sourceStorage.SetStorageGroup(group);
            }

            // Link all items to the group
            foreach (var member in itemsToLink)
            {
                if (member != sourceStorage)
                {
                    member.SetStorageGroup(group);
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a short label for a storage member suitable for announcements.
        /// </summary>
        /// <param name="storage">The storage member</param>
        /// <returns>Short label for the storage</returns>
        public static string GetStorageLabel(IStorageGroupMember storage)
        {
            if (storage is Thing thing)
            {
                return thing.LabelShort ?? thing.def?.label ?? (string)"RimWorldAccess.Building.Shelf.StorageFallback".Translate();
            }
            return "RimWorldAccess.Building.Shelf.StorageFallback".Translate();
        }

        /// <summary>
        /// Gets the storage group name for a storage member.
        /// </summary>
        /// <param name="storage">The storage member</param>
        /// <returns>The group label or "no group" if not in a group</returns>
        public static string GetGroupName(IStorageGroupMember storage)
        {
            if (storage?.Group == null)
                return "RimWorldAccess.Building.Shelf.NoGroup".Translate();
            return storage.Group.RenamableLabel ?? (string)"RimWorldAccess.Building.Shelf.UnnamedGroup".Translate();
        }

        /// <summary>
        /// Checks if two storage members can be linked (same tag).
        /// </summary>
        /// <param name="a">First storage member</param>
        /// <param name="b">Second storage member</param>
        /// <returns>True if they can be linked together</returns>
        public static bool CanLink(IStorageGroupMember a, IStorageGroupMember b)
        {
            if (a == null || b == null)
                return false;

            return a.StorageGroupTag == b.StorageGroupTag;
        }

        /// <summary>
        /// Gets the room that contains a storage member.
        /// </summary>
        /// <param name="storage">The storage member</param>
        /// <param name="map">The current map</param>
        /// <returns>The room containing the storage, or null if outdoors/invalid</returns>
        public static Room GetStorageRoom(IStorageGroupMember storage, Map map)
        {
            if (storage is Thing thing && map != null)
            {
                return thing.Position.GetRoom(map);
            }
            return null;
        }
    }
}
