using System;
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

        // Saved focus state for temporary category operations
        private static int savedCategoryIndex = -1;
        private static int savedSubcategoryIndex = -1;
        private static int savedItemIndex = -1;
        private static int savedBulkIndex = -1;

        // Temporary category tracking
        private static ScannerCategory temporaryCategory = null;

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
        /// Saves the current scanner focus state for later restoration.
        /// Used when temporarily switching to a different category (e.g., viewing obstacles).
        /// </summary>
        public static void SaveFocus()
        {
            savedCategoryIndex = currentCategoryIndex;
            savedSubcategoryIndex = currentSubcategoryIndex;
            savedItemIndex = currentItemIndex;
            savedBulkIndex = currentBulkIndex;
        }

        /// <summary>
        /// Restores the previously saved scanner focus state.
        /// Call this after removing a temporary category to return to the previous position.
        /// </summary>
        public static void RestoreFocus()
        {
            if (savedCategoryIndex >= 0)
            {
                currentCategoryIndex = savedCategoryIndex;
                currentSubcategoryIndex = savedSubcategoryIndex;
                currentItemIndex = savedItemIndex;
                currentBulkIndex = savedBulkIndex;

                // Reset saved state
                savedCategoryIndex = -1;
                savedSubcategoryIndex = -1;
                savedItemIndex = -1;
                savedBulkIndex = -1;

                // Validate indices in case the category list changed
                ValidateIndices();
            }
        }

        /// <summary>
        /// Creates a temporary category with the given name and items, and selects it.
        /// Only one temporary category can exist at a time.
        /// The temporary category is a proper scanner category that:
        /// - Appears in the category cycle (Ctrl+PageUp/Down)
        /// - Is preserved across RefreshItems() calls
        /// - Is cleared when Invalidate() is called (e.g., map switch)
        /// </summary>
        /// <param name="name">The name for the temporary category</param>
        /// <param name="items">The items to include in the category</param>
        public static void CreateTemporaryCategory(string name, List<ScannerItem> items)
        {
            // Remove any existing temporary category first
            RemoveTemporaryCategory();

            // Create the new temporary category with a single subcategory
            temporaryCategory = new ScannerCategory(name);
            var subcategory = new ScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            temporaryCategory.Subcategories.Add(subcategory);

            // Add to the categories list
            categories.Add(temporaryCategory);

            // Select the temporary category
            currentCategoryIndex = categories.Count - 1;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;
        }

        /// <summary>
        /// Removes the temporary category if one exists.
        /// Call RestoreFocus() after this to return to the previous scanner position.
        /// </summary>
        public static void RemoveTemporaryCategory()
        {
            if (temporaryCategory != null)
            {
                categories.Remove(temporaryCategory);
                temporaryCategory = null;
            }
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
        /// Invalidates the scanner cache, forcing a refresh on next access.
        /// Call this when switching maps or when map contents have significantly changed.
        /// Also clears any temporary category since it's no longer valid.
        /// </summary>
        public static void Invalidate()
        {
            categories.Clear();
            temporaryCategory = null;
            currentCategoryIndex = 0;
            currentSubcategoryIndex = 0;
            currentItemIndex = 0;
            currentBulkIndex = 0;

            // Also clear saved focus since it's no longer valid
            savedCategoryIndex = -1;
            savedSubcategoryIndex = -1;
            savedItemIndex = -1;
            savedBulkIndex = -1;

            // Clear any active search
            ScannerSearchState.ClearSearchSilent();
        }

        /// <summary>
        /// Refreshes the scanner item list based on current cursor position.
        /// Called automatically by navigation methods.
        /// If there's an active search filter, updates the filter with fresh items.
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

            var cursorPos = MapNavigationState.CurrentCursorPosition;

            // Check if there's an active search filter that needs refreshing
            if (ScannerSearchState.HasActiveFilter)
            {
                // Remember if user was in the filter category before refresh
                bool wasInFilterCategory = IsInTemporaryCategory();
                int previousItemIndex = currentItemIndex;

                // Get fresh filtered items
                var filteredItems = ScannerSearchState.RefreshMapFilter(map, cursorPos);
                if (filteredItems != null)
                {
                    // Collect regular items first
                    categories = ScannerHelper.CollectMapItems(map, cursorPos);

                    // Update the temporary category with fresh filtered items
                    if (filteredItems.Count > 0)
                    {
                        temporaryCategory = new ScannerCategory(ScannerSearchState.GetFilterCategoryName());
                        var subcategory = new ScannerSubcategory($"{ScannerSearchState.GetFilterCategoryName()}-All");
                        subcategory.Items.AddRange(filteredItems);
                        temporaryCategory.Subcategories.Add(subcategory);
                        categories.Add(temporaryCategory);

                        // If user was in filter category, keep them there
                        if (wasInFilterCategory)
                        {
                            currentCategoryIndex = categories.Count - 1; // Temp category is at end
                            currentSubcategoryIndex = 0;
                            // Try to preserve item index, clamping to valid range
                            currentItemIndex = Math.Min(previousItemIndex, filteredItems.Count - 1);
                            currentBulkIndex = 0;
                        }
                    }
                    else
                    {
                        // No matches - remove temporary category
                        temporaryCategory = null;

                        // If user was in filter category, restore their previous focus
                        if (wasInFilterCategory)
                        {
                            RestoreFocus();
                        }
                    }

                    if (categories.Count == 0)
                    {
                        TolkHelper.Speak("No items found on map", SpeechPriority.High);
                        return;
                    }

                    ValidateIndices();
                    return;
                }
            }

            // No active filter - standard refresh
            // Save temporary category before refresh (for non-filter temporary categories)
            var savedTemporaryCategory = temporaryCategory;

            // Collect items
            categories = ScannerHelper.CollectMapItems(map, cursorPos);

            // Re-add temporary category if it existed
            if (savedTemporaryCategory != null)
            {
                temporaryCategory = savedTemporaryCategory;
                categories.Add(temporaryCategory);
            }

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

            if (currentItem.IsTerrain || currentItem.HasTerrainRegions)
            {
                // For terrain regions, jump to the region center
                if (currentItem.HasTerrainRegions && currentBulkIndex < currentItem.TerrainRegions.Count)
                {
                    targetPosition = currentItem.TerrainRegions[currentBulkIndex].CenterPosition;
                }
                // Legacy: bulk terrain positions
                else if (currentItem.BulkTerrainPositions != null && currentBulkIndex < currentItem.BulkTerrainPositions.Count)
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
                if (currentItem.IsBulkGroup && currentItem.BulkThings != null && currentBulkIndex < currentItem.BulkThings.Count)
                {
                    targetThing = currentItem.BulkThings[currentBulkIndex];
                }
                if (targetThing == null)
                {
                    TolkHelper.Speak("Item no longer exists", SpeechPriority.High);
                    return;
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

        /// <summary>
        /// Gets the position of the currently selected item in the scanner.
        /// Used by visual preview patches to highlight the current item.
        /// Returns IntVec3.Invalid if no item is selected.
        /// </summary>
        public static IntVec3 GetCurrentItemPosition()
        {
            var currentItem = GetCurrentItem();
            if (currentItem == null)
                return IntVec3.Invalid;

            return currentItem.Position;
        }

        /// <summary>
        /// Checks if the scanner is currently focused on a temporary category.
        /// </summary>
        public static bool IsInTemporaryCategory()
        {
            return temporaryCategory != null && GetCurrentCategory() == temporaryCategory;
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

            if (currentItem.IsTerrain || currentItem.HasTerrainRegions)
            {
                // For terrain regions, use region center
                if (currentItem.HasTerrainRegions && currentBulkIndex < currentItem.TerrainRegions.Count)
                {
                    targetPos = currentItem.TerrainRegions[currentBulkIndex].CenterPosition;
                }
                // Legacy: bulk terrain positions
                else if (currentItem.BulkTerrainPositions != null && currentBulkIndex < currentItem.BulkTerrainPositions.Count)
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
                // Get the actual thing's LIVE position (considering bulk index)
                if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
                {
                    if (currentItem.BulkThings != null && currentBulkIndex < currentItem.BulkThings.Count)
                    {
                        Thing targetThing = currentItem.BulkThings[currentBulkIndex];
                        targetPos = targetThing.Position;
                    }
                    else
                    {
                        targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                    }
                }
                else
                {
                    // Use live position from Thing if available (for moving targets like pawns)
                    targetPos = currentItem.Thing?.Position ?? currentItem.Position;
                }
            }

            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var distance = (targetPos - cursorPos).LengthHorizontal;
            // Use our calculated targetPos for direction, not item.Position which may be stale
            var direction = GetDirectionFromCursor(targetPos);

            if (direction != null)
            {
                TolkHelper.Speak($"{distance:F1} tiles {direction}", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("here", SpeechPriority.Normal);
            }
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

        /// <summary>
        /// Calculates the compass direction from the cursor to a target position.
        /// Returns null if the cursor is at or very close to the target.
        /// </summary>
        private static string GetDirectionFromCursor(IntVec3 targetPosition)
        {
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            IntVec3 offset = targetPosition - cursorPos;

            if (offset.LengthHorizontal < 0.5f)
                return null; // Same position

            double angle = System.Math.Atan2(offset.x, offset.z) * (180.0 / System.Math.PI);
            if (angle < 0) angle += 360;

            if (angle >= 337.5 || angle < 22.5) return "North";
            if (angle >= 22.5 && angle < 67.5) return "Northeast";
            if (angle >= 67.5 && angle < 112.5) return "East";
            if (angle >= 112.5 && angle < 157.5) return "Southeast";
            if (angle >= 157.5 && angle < 202.5) return "South";
            if (angle >= 202.5 && angle < 247.5) return "Southwest";
            if (angle >= 247.5 && angle < 292.5) return "West";
            return "Northwest";
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

            // Special handling for terrain with regions
            if (item.HasTerrainRegions)
            {
                var region = item.TerrainRegions[currentBulkIndex];
                var cursorPos = MapNavigationState.CurrentCursorPosition;
                var distance = (region.CenterPosition - cursorPos).LengthHorizontal;
                var direction = GetDirectionFromCursor(region.CenterPosition);

                // Build size description - include quantity for deep ore deposits
                string sizeAndQuantity;
                if (region.TotalQuantity.HasValue && item.DeepOreDef != null)
                {
                    sizeAndQuantity = $"{region.TileCount} tiles ({region.TotalQuantity.Value} {item.DeepOreDef.label})";
                }
                else
                {
                    sizeAndQuantity = region.SizeDescription;
                }

                string announcement;
                if (direction != null)
                {
                    announcement = $"{item.Label}: {sizeAndQuantity}, {distance:F1} tiles {direction}";
                }
                else
                {
                    announcement = $"{item.Label}: {sizeAndQuantity}, here";
                }

                if (item.RegionCount > 1)
                {
                    int position = currentBulkIndex + 1;
                    announcement += $", region {position} of {item.RegionCount}";

                    // Show total across all regions only when there are multiple regions
                    if (item.HasQuantityInfo && item.DeepOreDef != null)
                    {
                        announcement += $", {item.TotalTileCount} tiles total ({item.TotalQuantityAcrossRegions} {item.DeepOreDef.label})";
                    }
                    else
                    {
                        announcement += $", {item.TotalTileCount} tiles total";
                    }
                }

                TolkHelper.Speak(announcement, SpeechPriority.Normal);
                return;
            }

            // Get target position (use live position for things that might be moving)
            IntVec3 targetPos = item.Thing != null ? item.Thing.Position : item.Position;
            var currentCursorPos = MapNavigationState.CurrentCursorPosition;
            var freshDistance = (targetPos - currentCursorPos).LengthHorizontal;  // Calculate fresh distance
            var itemDirection = GetDirectionFromCursor(targetPos);

            // Build announcement with direction (use comma instead of hyphen to avoid "minus" reading)
            string basicAnnouncement;
            if (itemDirection != null)
            {
                basicAnnouncement = $"{item.Label}, {freshDistance:F1} tiles {itemDirection}";
            }
            else
            {
                basicAnnouncement = $"{item.Label}, here";
            }

            // Add location context and activity for pawns (colonists, NPCs, animals)
            if (item.Thing is Pawn pawn)
            {
                string locationContext = TileInfoHelper.GetLocationContext(targetPos, Find.CurrentMap);
                if (!string.IsNullOrEmpty(locationContext))
                {
                    basicAnnouncement += $" {locationContext}";
                }

                // Add pawn activity if enabled in settings
                if (RimWorldAccessMod_Settings.Settings?.ShowPawnActivityOnMap ?? true)
                {
                    string activity = PawnHelper.GetPawnActivity(pawn);
                    if (!string.IsNullOrEmpty(activity))
                    {
                        basicAnnouncement += $", {activity}";
                    }
                }
            }

            // Add bulk count if this is a grouped item
            if (item.IsBulkGroup)
            {
                int position = currentBulkIndex + 1;
                basicAnnouncement += $", {position} of {item.BulkCount}";
            }

            TolkHelper.Speak(basicAnnouncement, SpeechPriority.Normal);
        }

        private static void AnnounceCurrentBulkItem()
        {
            var item = GetCurrentItem();
            if (item == null || !item.IsBulkGroup)
                return;

            if (currentBulkIndex < 0 || currentBulkIndex >= item.BulkCount)
                return;

            // For terrain regions (adjacency-grouped)
            if (item.HasTerrainRegions)
            {
                if (currentBulkIndex >= item.TerrainRegions.Count)
                    return;

                var region = item.TerrainRegions[currentBulkIndex];
                var regionDistance = (region.CenterPosition - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
                var regionDirection = GetDirectionFromCursor(region.CenterPosition);
                int regionPosition = currentBulkIndex + 1;

                // Build size description - include quantity for deep ore deposits
                string sizeAndQuantity;
                if (region.TotalQuantity.HasValue && item.DeepOreDef != null)
                {
                    sizeAndQuantity = $"{region.TileCount} tiles ({region.TotalQuantity.Value} {item.DeepOreDef.label})";
                }
                else
                {
                    sizeAndQuantity = region.SizeDescription;
                }

                string announcement;
                if (regionDirection != null)
                {
                    announcement = $"{item.Label}: {sizeAndQuantity}, {regionDistance:F1} tiles {regionDirection}, region {regionPosition} of {item.RegionCount}";
                }
                else
                {
                    announcement = $"{item.Label}: {sizeAndQuantity}, here, region {regionPosition} of {item.RegionCount}";
                }
                TolkHelper.Speak(announcement, SpeechPriority.Normal);
                return;
            }

            // For legacy terrain bulk groups (non-adjacent grouping)
            if (item.IsTerrain && item.BulkTerrainPositions != null)
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
                var desTargetPos = targetDesignation.target.Cell;
                var desDistance = (desTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
                var desDirection = GetDirectionFromCursor(desTargetPos);
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

                if (desDirection != null)
                {
                    TolkHelper.Speak($"{designationLabel}, {desDistance:F1} tiles {desDirection}, {desPosition} of {item.BulkCount}", SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak($"{designationLabel}, here, {desPosition} of {item.BulkCount}", SpeechPriority.Normal);
                }
                return;
            }

            // For thing bulk groups, get label from the actual thing at this index
            if (item.BulkThings == null || currentBulkIndex >= item.BulkThings.Count)
                return;

            var targetThing = item.BulkThings[currentBulkIndex];
            if (targetThing == null)
                return;

            var thingTargetPos = targetThing.Position;
            var distance = (thingTargetPos - MapNavigationState.CurrentCursorPosition).LengthHorizontal;
            var thingDirection = GetDirectionFromCursor(thingTargetPos);
            var position = currentBulkIndex + 1;

            // Build label from this specific thing, not the group label
            string thingLabel = targetThing.LabelShort ?? targetThing.def?.label ?? item.Label;

            if (thingDirection != null)
            {
                TolkHelper.Speak($"{thingLabel}, {distance:F1} tiles {thingDirection}, {position} of {item.BulkCount}", SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak($"{thingLabel}, here, {position} of {item.BulkCount}", SpeechPriority.Normal);
            }
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
