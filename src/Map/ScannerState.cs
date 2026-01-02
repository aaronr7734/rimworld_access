using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class ScannerState
    {
        private static List<ScannerCategory> categories = new List<ScannerCategory>();
        private static int currentCategoryIndex = 0;
        private static int currentSubcategoryIndex = 0;
        private static int currentItemIndex = 0;
        private static int currentBulkIndex = 0; // Index within a bulk group
        private static bool autoJumpMode = false; // Auto-jump to items when navigating

        /// <summary>
        /// Toggles auto-jump mode on/off (Alt+Home).
        /// When enabled, cursor automatically jumps to items as you navigate.
        /// </summary>
        public static void ToggleAutoJumpMode()
        {
            autoJumpMode = !autoJumpMode;
            string status = autoJumpMode ? "enabled" : "disabled";
            TolkHelper.Speak($"Auto-jump mode {status}", SpeechPriority.High);
        }

        /// <summary>
        /// Recalculates distances for all items from the current cursor position.
        /// Does NOT re-sort items or refresh the list from the map.
        /// </summary>
        private static void RecalculateDistances()
        {
            if (!MapNavigationState.IsInitialized)
                return;

            var cursorPos = MapNavigationState.CurrentCursorPosition;

            foreach (var category in categories)
            {
                foreach (var subcat in category.Subcategories)
                {
                    foreach (var item in subcat.Items)
                    {
                        // Recalculate distance for the primary position
                        if (item.IsTerrain)
                        {
                            item.Distance = (item.Position - cursorPos).LengthHorizontal;
                        }
                        else if (item.Thing != null)
                        {
                            item.Distance = (item.Thing.Position - cursorPos).LengthHorizontal;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the scanner item list based on current cursor position.
        /// Called automatically by navigation methods.
        /// </summary>
        private static void RefreshItems()
        {
            if (!MapNavigationState.IsInitialized)
            {
                TolkHelper.Speak("Map navigation not initialized", SpeechPriority.High);
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("No active map", SpeechPriority.High);
                return;
            }

            // Collect items
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            categories = ScannerHelper.CollectMapItems(map, cursorPos);

            if (categories.Count == 0)
            {
                TolkHelper.Speak("No items found on map", SpeechPriority.High);
                return;
            }

            // Validate and adjust indices if needed
            ValidateIndices();
        }

        public static void NextItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex++;
            if (currentItemIndex >= currentSubcat.Items.Count)
            {
                currentItemIndex = 0; // Wrap to first item
            }

            currentBulkIndex = 0; // Reset bulk index when changing items

            // Recalculate distances from current cursor position
            RecalculateDistances();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        public static void PreviousItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex--;
            if (currentItemIndex < 0)
            {
                currentItemIndex = currentSubcat.Items.Count - 1; // Wrap to last item
            }

            currentBulkIndex = 0; // Reset bulk index when changing items

            // Recalculate distances from current cursor position
            RecalculateDistances();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        public static void NextBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex++;
            if (currentBulkIndex >= currentItem.BulkCount)
            {
                currentBulkIndex = 0; // Wrap to first bulk item
            }

            // Recalculate distances from current cursor position
            RecalculateDistances();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

        public static void PreviousBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex--;
            if (currentBulkIndex < 0)
            {
                currentBulkIndex = currentItem.BulkCount - 1; // Wrap to last bulk item
            }

            // Recalculate distances from current cursor position
            RecalculateDistances();

            // Auto-jump if enabled
            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

        public static void NextCategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            currentCategoryIndex++;
            if (currentCategoryIndex >= categories.Count)
            {
                currentCategoryIndex = 0; // Wrap to first category
            }

            // Reset subcategory and item indices
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;

            // Skip empty subcategories
            SkipEmptySubcategories(forward: true);

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        public static void PreviousCategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            currentCategoryIndex--;
            if (currentCategoryIndex < 0)
            {
                currentCategoryIndex = categories.Count - 1; // Wrap to last category
            }

            // Reset subcategory and item indices
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;

            // Skip empty subcategories
            SkipEmptySubcategories(forward: true);

            AnnounceCurrentCategory();
            AnnounceCurrentItem();
        }

        public static void NextSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex++;
                if (currentSubcategoryIndex >= currentCategory.Subcategories.Count)
                {
                    currentSubcategoryIndex = 0; // Wrap to first subcategory
                }

                // Break if we've cycled through all subcategories
                if (currentSubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            // Reset item index
            currentItemIndex = 0;

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void PreviousSubcategory()
        {
            if (WorldNavigationState.IsActive) return;

            RefreshItems();
            if (categories.Count == 0) return;

            var currentCategory = GetCurrentCategory();
            if (currentCategory == null) return;

            int startIndex = currentSubcategoryIndex;
            do
            {
                currentSubcategoryIndex--;
                if (currentSubcategoryIndex < 0)
                {
                    currentSubcategoryIndex = currentCategory.Subcategories.Count - 1; // Wrap to last subcategory
                }

                // Break if we've cycled through all subcategories
                if (currentSubcategoryIndex == startIndex)
                    break;

            } while (GetCurrentSubcategory()?.IsEmpty ?? true);

            // Reset item index
            currentItemIndex = 0;

            AnnounceCurrentSubcategory();
            AnnounceCurrentItem();
        }

        public static void JumpToCurrent()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            IntVec3 targetPosition;

            if (currentItem.IsTerrain)
            {
                // For terrain, check if we're navigating bulk terrain positions
                if (currentItem.BulkTerrainPositions != null && currentBulkIndex < currentItem.BulkTerrainPositions.Count)
                {
                    targetPosition = currentItem.BulkTerrainPositions[currentBulkIndex];
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsDesignation)
            {
                // For designations, check if we're navigating bulk designations
                if (currentItem.BulkDesignations != null && currentBulkIndex < currentItem.BulkDesignations.Count)
                {
                    targetPosition = currentItem.BulkDesignations[currentBulkIndex].target.Cell;
                }
                else
                {
                    targetPosition = currentItem.Position;
                }
            }
            else if (currentItem.IsZone)
            {
                // For zones, use the calculated center position
                targetPosition = currentItem.Position;
            }
            else if (currentItem.IsRoom)
            {
                // For rooms, use the calculated center position
                targetPosition = currentItem.Position;
            }
            else
            {
                // Get the actual thing to jump to (considering bulk index)
                Thing targetThing = currentItem.Thing;
                if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
                {
                    targetThing = currentItem.BulkThings[currentBulkIndex];
                }
                targetPosition = targetThing.Position;
            }

            // Update map cursor position
            MapNavigationState.CurrentCursorPosition = targetPosition;

            // Jump camera to position
            Find.CameraDriver.JumpToCurrentMapLoc(targetPosition);

            // Announce the item being jumped to
            if (autoJumpMode)
            {
                // In auto-jump mode, announce item details
                if (currentItem.IsBulkGroup)
                {
                    AnnounceCurrentBulkItem();
                }
                else
                {
                    AnnounceCurrentItem();
                }
            }
            else
            {
                // Manual jump (Home key) - just announce the jump
                TolkHelper.Speak($"Jumped to {currentItem.Label}", SpeechPriority.Normal);
            }
        }

        public static void ReadDistanceAndDirection()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            // Recalculate distances from current cursor position
            RecalculateDistances();

            IntVec3 targetPos;

            if (currentItem.IsTerrain)
            {
                // For terrain, check if we're navigating bulk terrain positions
                if (currentItem.BulkTerrainPositions != null && currentBulkIndex < currentItem.BulkTerrainPositions.Count)
                {
                    targetPos = currentItem.BulkTerrainPositions[currentBulkIndex];
                }
                else
                {
                    targetPos = currentItem.Position;
                }
            }
            else
            {
                // Get the actual thing (considering bulk index)
                targetPos = currentItem.Position;
                if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
                {
                    Thing targetThing = currentItem.BulkThings[currentBulkIndex];
                    targetPos = targetThing.Position;
                }
            }

            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var distance = (targetPos - cursorPos).LengthHorizontal;
            var direction = currentItem.GetDirectionFrom(cursorPos);

            TolkHelper.Speak($"{distance:F1} tiles, {direction}", SpeechPriority.Normal);
        }

        private static ScannerCategory GetCurrentCategory()
        {
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
                return null;

            return categories[currentCategoryIndex];
        }

        private static ScannerSubcategory GetCurrentSubcategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return null;

            if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                return null;

            return category.Subcategories[currentSubcategoryIndex];
        }

        private static ScannerItem GetCurrentItem()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return null;

            if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                return null;

            return subcat.Items[currentItemIndex];
        }

        private static void ValidateIndices()
        {
            // Ensure category index is valid
            if (currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
            {
                currentCategoryIndex = 0;
            }

            // Ensure subcategory index is valid and not empty
            var category = GetCurrentCategory();
            if (category != null)
            {
                if (currentSubcategoryIndex < 0 || currentSubcategoryIndex >= category.Subcategories.Count)
                {
                    currentSubcategoryIndex = 0;
                }

                // Skip to first non-empty subcategory
                SkipEmptySubcategories(forward: true);
            }

            // Ensure item index is valid
            var subcat = GetCurrentSubcategory();
            if (subcat != null)
            {
                if (currentItemIndex < 0 || currentItemIndex >= subcat.Items.Count)
                {
                    currentItemIndex = 0;
                }
            }
        }

        private static void SkipEmptySubcategories(bool forward)
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            int startIndex = currentSubcategoryIndex;
            int attempts = 0;
            int maxAttempts = category.Subcategories.Count;

            while ((GetCurrentSubcategory()?.IsEmpty ?? true) && attempts < maxAttempts)
            {
                if (forward)
                {
                    currentSubcategoryIndex++;
                    if (currentSubcategoryIndex >= category.Subcategories.Count)
                    {
                        currentSubcategoryIndex = 0;
                    }
                }
                else
                {
                    currentSubcategoryIndex--;
                    if (currentSubcategoryIndex < 0)
                    {
                        currentSubcategoryIndex = category.Subcategories.Count - 1;
                    }
                }

                attempts++;
            }

            // If all subcategories are empty, reset to start
            if (GetCurrentSubcategory()?.IsEmpty ?? true)
            {
                currentSubcategoryIndex = startIndex;
            }
        }

        private static void AnnounceCurrentCategory()
        {
            var category = GetCurrentCategory();
            if (category == null) return;

            TolkHelper.Speak($"{category.Name} - {category.TotalItemCount} items", SpeechPriority.Normal);
        }

        private static void AnnounceCurrentSubcategory()
        {
            var subcat = GetCurrentSubcategory();
            if (subcat == null) return;

            TolkHelper.Speak($"{subcat.Name} - {subcat.Items.Count} items", SpeechPriority.Normal);
        }

        private static void AnnounceCurrentItem()
        {
            var item = GetCurrentItem();
            if (item == null)
            {
                TolkHelper.Speak("No items in this category", SpeechPriority.Normal);
                return;
            }

            // Build announcement without position info
            string announcement = $"{item.Label} - {item.Distance:F1} tiles";

            // Add bulk count if this is a grouped item
            if (item.IsBulkGroup)
            {
                int position = currentBulkIndex + 1;
                announcement += $", {position} of {item.BulkCount}";
            }

            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        private static void AnnounceCurrentBulkItem()
        {
            var item = GetCurrentItem();
            if (item == null || !item.IsBulkGroup)
                return;

            if (currentBulkIndex < 0 || currentBulkIndex >= item.BulkCount)
                return;

            // For terrain bulk groups, we don't have individual things
            if (item.IsTerrain)
            {
                var terrainPosition = currentBulkIndex + 1;
                TolkHelper.Speak($"{item.Label} - {terrainPosition} of {item.BulkCount}", SpeechPriority.Normal);
                return;
            }

            // For designation bulk groups
            if (item.IsDesignation)
            {
                if (item.BulkDesignations == null || currentBulkIndex >= item.BulkDesignations.Count)
                    return;

                var targetDesignation = item.BulkDesignations[currentBulkIndex];
                var desCursorPos = MapNavigationState.CurrentCursorPosition;
                var desDistance = (targetDesignation.target.Cell - desCursorPos).LengthHorizontal;
                var desPosition = currentBulkIndex + 1;

                // Build label from the specific designation target
                string designationLabel;
                if (targetDesignation.target.HasThing && targetDesignation.target.Thing != null)
                {
                    designationLabel = targetDesignation.target.Thing.LabelShort;
                }
                else
                {
                    // For cell-based designations, use the main item label
                    designationLabel = item.Label;
                }

                TolkHelper.Speak($"{designationLabel} - {desDistance:F1} tiles, {desPosition} of {item.BulkCount}", SpeechPriority.Normal);
                return;
            }

            // For thing bulk groups, get label from the actual thing at this index
            if (item.BulkThings == null || currentBulkIndex >= item.BulkThings.Count)
                return;

            var targetThing = item.BulkThings[currentBulkIndex];
            if (targetThing == null)
                return;

            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var distance = (targetThing.Position - cursorPos).LengthHorizontal;
            var position = currentBulkIndex + 1;

            // Build label from this specific thing, not the group label
            string thingLabel = targetThing.LabelShort ?? targetThing.def?.label ?? item.Label;

            TolkHelper.Speak($"{thingLabel} - {distance:F1} tiles, {position} of {item.BulkCount}", SpeechPriority.Normal);
        }

        /// <summary>
        /// Jumps to the first item in the current subcategory.
        /// </summary>
        public static void JumpToFirstItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex = 0;
            currentBulkIndex = 0;
            RecalculateDistances();

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// Jumps to the last item in the current subcategory.
        /// </summary>
        public static void JumpToLastItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Initialize scanner if not already done
            if (categories.Count == 0)
            {
                RefreshItems();
                if (categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex = currentSubcat.Items.Count - 1;
            currentBulkIndex = 0;
            RecalculateDistances();

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }
        }
    }
}
