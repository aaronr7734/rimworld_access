using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for manual storage linking selection mode.
    /// Uses the map cursor for navigation (like transport pod selection).
    /// Space toggles storage selection at cursor, Enter confirms linking.
    /// </summary>
    public static class ShelfLinkingState
    {
        /// <summary>
        /// Whether storage linking selection mode is currently active.
        /// </summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// The map where selection is happening.
        /// </summary>
        private static Map currentMap;

        /// <summary>
        /// The source storage that initiated linking mode.
        /// </summary>
        private static IStorageGroupMember sourceStorage;

        /// <summary>
        /// The storage group tag for compatibility checking.
        /// </summary>
        private static string sourceTag;

        /// <summary>
        /// Set of storage members currently selected for linking.
        /// </summary>
        private static HashSet<IStorageGroupMember> selectedStorage;

        /// <summary>
        /// Opens manual storage linking selection mode.
        /// </summary>
        /// <param name="source">The source storage to link from</param>
        public static void Open(IStorageGroupMember source)
        {
            if (!GuardHelper.RequireMap(SpeechPriority.High)) return;

            if (source == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.NoStorageSelected".Translate(), SpeechPriority.High);
                return;
            }

            currentMap = Find.CurrentMap;
            sourceStorage = source;
            sourceTag = source.StorageGroupTag;
            selectedStorage = new HashSet<IStorageGroupMember>();

            // Pre-select the source storage
            selectedStorage.Add(source);

            // Clear game selection and select source thing
            if (source is Thing sourceThing)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(sourceThing, playSound: false, forceDesignatorDeselect: false);
            }

            IsActive = true;

            string sourceLabel = ShelfLinkingHelper.GetStorageLabel(source);
            TolkHelper.Speak("RimWorldAccess.Building.Shelf.OpenPrompt".Translate(sourceLabel), SpeechPriority.High);
        }

        /// <summary>
        /// Closes storage linking selection mode without linking.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentMap = null;
            sourceStorage = null;
            sourceTag = null;
            selectedStorage = null;

            Find.Selector.ClearSelection();

            TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingCancelled".Translate(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Handles keyboard input for storage linking mode.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive)
                return false;

            // Space - toggle storage selection at cursor
            if (key == KeyCode.Space && !shift && !ctrl && !alt)
            {
                ToggleStorageAtCursor();
                return true;
            }

            // Enter - confirm and link selected storage
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ConfirmSelection();
                return true;
            }

            // Escape - cancel selection mode
            if (key == KeyCode.Escape)
            {
                Close();
                return true;
            }

            // Let arrow keys pass through to map navigation
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Toggles selection of storage at the current cursor position.
        /// </summary>
        private static void ToggleStorageAtCursor()
        {
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            if (!cursorPos.InBounds(currentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.InvalidPosition".Translate(), SpeechPriority.Normal);
                return;
            }

            // Find compatible storage at cursor
            var storage = ShelfLinkingHelper.GetStorageAt(cursorPos, sourceTag, currentMap);

            if (storage == null)
            {
                // Check if there's any storage at all (wrong tag)
                var anyStorage = ShelfLinkingHelper.GetAllStorageAt(cursorPos, currentMap);
                if (anyStorage.Count > 0)
                {
                    string label = ShelfLinkingHelper.GetStorageLabel(anyStorage[0]);
                    TolkHelper.Speak("RimWorldAccess.Building.Shelf.IncompatibleStorageType".Translate(label), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Shelf.NoStorageHere".Translate(), SpeechPriority.Normal);
                }
                return;
            }

            // Don't allow deselecting the source
            if (storage == sourceStorage)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.SourceCannotDeselect".Translate(), SpeechPriority.Normal);
                return;
            }

            string storageLabel = ShelfLinkingHelper.GetStorageLabel(storage);

            if (selectedStorage.Contains(storage))
            {
                // Deselect
                selectedStorage.Remove(storage);
                if (storage is Thing thing)
                {
                    Find.Selector.Deselect(thing);
                }
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.StorageDeselected".Translate(storageLabel, selectedStorage.Count), SpeechPriority.Normal);
            }
            else
            {
                // Select
                selectedStorage.Add(storage);
                if (storage is Thing thing)
                {
                    Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                }
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.StorageSelected".Translate(storageLabel, selectedStorage.Count), SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Confirms selection and links all selected storage.
        /// </summary>
        private static void ConfirmSelection()
        {
            if (selectedStorage.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.SelectAtLeastOneOther".Translate(), SpeechPriority.High);
                return;
            }

            // Check for items already in different groups
            var alreadyLinked = ShelfLinkingHelper.GetAlreadyLinkedItems(
                selectedStorage.ToList(), sourceStorage.Group);

            if (alreadyLinked.Count > 0)
            {
                // Show confirmation dialog
                ShelfLinkingConfirmDialog.Show(
                    alreadyLinked,
                    onYes: () => PerformLinking(),
                    onNo: () =>
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingCancelledStillSelecting".Translate(), SpeechPriority.Normal);
                    });
            }
            else
            {
                // No conflicts, link directly
                PerformLinking();
            }
        }

        /// <summary>
        /// Performs the actual linking operation.
        /// </summary>
        private static void PerformLinking()
        {
            int count = selectedStorage.Count;
            string countStr = ShelfLinkingHelper.FormatStorageCount(selectedStorage.ToList());

            bool success = ShelfLinkingHelper.LinkStorageItems(
                sourceStorage, selectedStorage.ToList(), currentMap);

            // Close the state
            IsActive = false;
            currentMap = null;
            var source = sourceStorage;
            sourceStorage = null;
            sourceTag = null;
            selectedStorage = null;

            Find.Selector.ClearSelection();

            if (success)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkedCount".Translate(countStr), SpeechPriority.High);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingFailed".Translate(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// Gets the currently selected storage items (for visual feedback).
        /// </summary>
        public static IEnumerable<IStorageGroupMember> GetSelectedStorage()
        {
            if (!IsActive || selectedStorage == null)
                yield break;

            foreach (var storage in selectedStorage)
            {
                yield return storage;
            }
        }

        /// <summary>
        /// Gets the source storage (for visual feedback).
        /// </summary>
        public static IStorageGroupMember GetSourceStorage()
        {
            return IsActive ? sourceStorage : null;
        }

        /// <summary>
        /// Gets the storage tag being used for compatibility (for external checks).
        /// </summary>
        public static string GetSourceTag()
        {
            return IsActive ? sourceTag : null;
        }

        /// <summary>
        /// Checks if any selected storage occupies the given position.
        /// Used for announcements during linking mode.
        /// </summary>
        /// <param name="position">The position to check</param>
        /// <returns>True if selected storage is at this position</returns>
        public static bool IsStorageSelectedAt(IntVec3 position)
        {
            if (!IsActive || selectedStorage == null || currentMap == null)
                return false;

            foreach (var storage in selectedStorage)
            {
                if (storage is Thing thing && thing.Spawned)
                {
                    if (thing.OccupiedRect().Contains(position))
                        return true;
                }
            }
            return false;
        }
    }
}
