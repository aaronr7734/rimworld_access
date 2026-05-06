using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for shelf/storage linking accessibility.
    /// Injects custom gizmos for linking storage buildings without mouse-based multi-select.
    /// </summary>
    public static class ShelfLinkingPatch
    {
        /// <summary>
        /// Shared gizmo generation for storage group members (Building_Storage, Blueprint_Storage, etc.)
        /// </summary>
        /// <param name="result">Original gizmos to filter</param>
        /// <param name="storageMember">The storage group member</param>
        /// <param name="thing">The underlying Thing for position/map access</param>
        /// <returns>Filtered gizmos plus accessible alternatives</returns>
        private static IEnumerable<Gizmo> ProcessStorageGizmos(IEnumerable<Gizmo> result, IStorageGroupMember storageMember, Thing thing)
        {
            // Don't add gizmos if selection mode is already active
            if (ShelfLinkingState.IsActive)
            {
                foreach (var gizmo in result)
                    yield return gizmo;
                yield break;
            }

            if (storageMember == null)
            {
                foreach (var gizmo in result)
                    yield return gizmo;
                yield break;
            }

            Map map = thing.Map;
            if (map == null)
            {
                foreach (var gizmo in result)
                    yield return gizmo;
                yield break;
            }

            string storageTag = storageMember.StorageGroupTag;

            // Filter original gizmos: hide game's storage linking gizmos and capture tooltip
            string gameLinkTooltip = "";

            foreach (var gizmo in result)
            {
                // Hide "Select <itemname>" gizmos - not useful for screen reader users
                if (gizmo is Command_SelectStorage)
                    continue;

                if (gizmo is Command cmd)
                {
                    string label = cmd.Label?.ToLower() ?? cmd.defaultLabel?.ToLower() ?? "";

                    // Hide "Link settings" - we replace it with accessible alternatives
                    // Note: Label is "Link settings" NOT "Link storage settings"
                    if (label == "link settings")
                    {
                        // Capture description for our gizmos, strip any color tags
                        gameLinkTooltip = (cmd.Desc ?? cmd.defaultDesc ?? "").StripTags();
                        continue;
                    }

                    // Hide "Select all linked" - doesn't do anything the mod supports (mouse-based multi-select)
                    if (label.Contains("select all linked"))
                        continue;

                    // Keep "Unlink storage settings" visible - it's useful for unlinking shelves!
                }

                yield return gizmo;
            }

            // === Gizmo 1: Link all storage in room ===
            Room room = thing.Position.GetRoom(map);
            bool hasProperRoom = room != null && room.ProperRoom;

            if (hasProperRoom)
            {
                var roomStorage = ShelfLinkingHelper.GetCompatibleStorageInRoom(
                    room, storageTag, map, storageMember);

                // Only show if there's at least one other storage in the room
                if (roomStorage.Count > 0)
                {
                    // Categorize storage by group status
                    StorageGroup sourceGroup = storageMember.Group;
                    var newItems = new List<IStorageGroupMember>();      // Not in any group
                    var sameGroupItems = new List<IStorageGroupMember>(); // Already in source's group
                    var differentGroupItems = new List<IStorageGroupMember>(); // In a different group

                    foreach (var item in roomStorage)
                    {
                        if (item.Group == null)
                            newItems.Add(item);
                        else if (sourceGroup != null && item.Group == sourceGroup)
                            sameGroupItems.Add(item);
                        else
                            differentGroupItems.Add(item);
                    }

                    // Items that would actually be linked (new + different group)
                    var itemsToLink = new List<IStorageGroupMember>(newItems);
                    itemsToLink.AddRange(differentGroupItems);

                    // Only show gizmo if there are items to potentially add
                    if (itemsToLink.Count > 0)
                    {
                        // Build a descriptive label
                        string gizmoLabel;
                        string countStr = ShelfLinkingHelper.FormatStorageCount(itemsToLink);

                        if (sameGroupItems.Count > 0)
                        {
                            // Some already in group, some new
                            string alreadyStr = ShelfLinkingHelper.FormatStorageCount(sameGroupItems);
                            gizmoLabel = $"Add {countStr} to group ({alreadyStr} already linked)";
                        }
                        else if (sourceGroup != null)
                        {
                            // Source is in a group, adding new items
                            gizmoLabel = $"Add {countStr} to group";
                        }
                        else
                        {
                            // Creating a new group - clarify these are OTHER shelves
                            gizmoLabel = $"Link with {countStr} in room";
                        }

                        // Use game's tooltip if captured, otherwise fall back to our description
                        string roomGizmoDesc = !string.IsNullOrEmpty(gameLinkTooltip)
                            ? gameLinkTooltip
                            : "Link all compatible storage in this room together. They will share the same storage settings.";

                        // Capture for delegate
                        IStorageGroupMember memberCapture = storageMember;
                        List<IStorageGroupMember> itemsCapture = itemsToLink;
                        List<IStorageGroupMember> differentCapture = differentGroupItems;
                        Map mapCapture = map;

                        var roomGizmo = new Command_Action
                        {
                            defaultLabel = gizmoLabel,
                            defaultDesc = roomGizmoDesc,
                            icon = ContentFinder<Texture2D>.Get("UI/Commands/CopySettings", true),
                            action = delegate
                            {
                                LinkAllInRoom(memberCapture, itemsCapture, differentCapture, mapCapture);
                            }
                        };

                        yield return roomGizmo;
                    }
                }
            }

            // === Gizmo 2: Manual selection mode ===
            // Use game's tooltip if captured, otherwise fall back to our description
            string manualGizmoDesc = !string.IsNullOrEmpty(gameLinkTooltip)
                ? gameLinkTooltip
                : "Enter selection mode to manually choose which storage buildings to link together.";

            // Capture for delegate
            IStorageGroupMember manualMemberCapture = storageMember;

            var manualGizmo = new Command_Action
            {
                defaultLabel = "Link storage manually",
                defaultDesc = manualGizmoDesc,
                icon = ContentFinder<Texture2D>.Get("UI/Commands/CopySettings", true),
                action = delegate
                {
                    ShelfLinkingState.Open(manualMemberCapture);
                }
            };

            yield return manualGizmo;

            // === Gizmo 3: Rename/Name storage group ===
            string renameLabel = storageMember.Group != null
                ? $"Rename {storageMember.Group.RenamableLabel}"
                : "Rename";

            // Capture for delegate
            IStorageGroupMember renameMemberCapture = storageMember;

            var renameGizmo = new Command_Action
            {
                defaultLabel = renameLabel,
                defaultDesc = "",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Rename", true),
                action = delegate { StorageRenameState.Open(renameMemberCapture); }
            };
            yield return renameGizmo;

            // === Gizmo 4: Direct storage settings access ===
            // Capture for delegate
            IStorageGroupMember settingsMemberCapture = storageMember;

            var settingsGizmo = new Command_Action
            {
                defaultLabel = "Storage settings",
                defaultDesc = "",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SetTargetFuelLevel", true),
                action = delegate
                {
                    StorageSettings settings = settingsMemberCapture.StoreSettings;
                    if (settings != null)
                        StorageSettingsMenuState.Open(settings);
                }
            };
            yield return settingsGizmo;
        }

        /// <summary>
        /// Patch to add custom storage linking gizmos to Building_Storage.
        /// </summary>
        [HarmonyPatch(typeof(Building_Storage))]
        [HarmonyPatch("GetGizmos")]
        public static class Building_Storage_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building_Storage __instance)
            {
                return ProcessStorageGizmos(__result, __instance as IStorageGroupMember, __instance);
            }
        }

        /// <summary>
        /// Patch to add custom storage linking gizmos to Blueprint_Storage.
        /// This allows linking storage blueprints before they are built.
        /// </summary>
        [HarmonyPatch(typeof(Blueprint_Storage))]
        [HarmonyPatch("GetGizmos")]
        public static class Blueprint_Storage_GetGizmos_Patch
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Blueprint_Storage __instance)
            {
                return ProcessStorageGizmos(__result, __instance as IStorageGroupMember, __instance);
            }
        }

        /// <summary>
        /// Links storage items, showing confirmation if any are in different groups.
        /// </summary>
        /// <param name="source">The source storage member</param>
        /// <param name="itemsToLink">Items to add to the group (new + different group items)</param>
        /// <param name="differentGroupItems">Subset of items currently in a different group (need confirmation)</param>
        /// <param name="map">The current map</param>
        private static void LinkAllInRoom(IStorageGroupMember source, List<IStorageGroupMember> itemsToLink, List<IStorageGroupMember> differentGroupItems, Map map)
        {
            if (differentGroupItems.Count > 0)
            {
                // Show confirmation for items being moved from other groups
                ShelfLinkingConfirmDialog.Show(
                    differentGroupItems,
                    onYes: () =>
                    {
                        PerformRoomLinking(source, itemsToLink, map);
                    },
                    onNo: () =>
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingCancelledShort".Translate(), SpeechPriority.Normal);
                    });
            }
            else
            {
                // No conflicts (all items are unlinked), link directly
                PerformRoomLinking(source, itemsToLink, map);
            }
        }

        /// <summary>
        /// Performs the actual linking operation.
        /// </summary>
        private static void PerformRoomLinking(IStorageGroupMember source, List<IStorageGroupMember> itemsToLink, Map map)
        {
            string countStr = ShelfLinkingHelper.FormatStorageCount(itemsToLink);

            bool success = ShelfLinkingHelper.LinkStorageItems(source, itemsToLink, map);

            if (success)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.AddedToGroup".Translate(countStr), SpeechPriority.High);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingFailed".Translate(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// Patch for SelectionDrawer to draw visual feedback during storage linking mode.
        /// Shows green highlights for selected storage and yellow highlight at cursor.
        /// </summary>
        [HarmonyPatch(typeof(SelectionDrawer))]
        [HarmonyPatch("DrawSelectionOverlays")]
        public static class SelectionDrawer_ShelfLinking_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (!ShelfLinkingState.IsActive)
                    return;

                Map map = Find.CurrentMap;
                if (map == null)
                    return;

                // Draw highlights for selected storage (green)
                foreach (var storage in ShelfLinkingState.GetSelectedStorage())
                {
                    if (storage is Thing thing && thing.Spawned)
                    {
                        // Get all cells occupied by the building
                        var cells = thing.OccupiedRect().Cells.ToList();
                        GenDraw.DrawFieldEdges(cells, Color.green);
                    }
                }

                // Draw highlight at cursor position (yellow)
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (cursorPos.InBounds(map))
                {
                    // Check if there's compatible storage at cursor
                    string tag = ShelfLinkingState.GetSourceTag();
                    var storageAtCursor = ShelfLinkingHelper.GetStorageAt(cursorPos, tag, map);

                    if (storageAtCursor is Thing cursorThing && cursorThing.Spawned)
                    {
                        // Highlight the whole building in yellow
                        var cells = cursorThing.OccupiedRect().Cells.ToList();
                        GenDraw.DrawFieldEdges(cells, Color.yellow);
                    }
                    else
                    {
                        // Just highlight cursor cell
                        GenDraw.DrawFieldEdges(new List<IntVec3> { cursorPos }, Color.yellow);
                    }
                }
            }
        }
    }
}
