using System.Collections.Generic;
using System.Linq;
using RimWorld;
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
            RefreshItems();
            if (categories.Count == 0) return;

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex++;
            if (currentItemIndex >= currentSubcat.Items.Count)
            {
                currentItemIndex = 0; // Wrap to first item
            }

            currentBulkIndex = 0; // Reset bulk index when changing items
            AnnounceCurrentItem();
        }

        public static void PreviousItem()
        {
            RefreshItems();
            if (categories.Count == 0) return;

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            currentItemIndex--;
            if (currentItemIndex < 0)
            {
                currentItemIndex = currentSubcat.Items.Count - 1; // Wrap to last item
            }

            currentBulkIndex = 0; // Reset bulk index when changing items
            AnnounceCurrentItem();
        }

        public static void NextBulkItem()
        {
            RefreshItems();
            if (categories.Count == 0) return;

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex++;
            if (currentBulkIndex >= currentItem.BulkCount)
            {
                currentBulkIndex = 0; // Wrap to first bulk item
            }

            AnnounceCurrentBulkItem();
        }

        public static void PreviousBulkItem()
        {
            RefreshItems();
            if (categories.Count == 0) return;

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            currentBulkIndex--;
            if (currentBulkIndex < 0)
            {
                currentBulkIndex = currentItem.BulkCount - 1; // Wrap to last bulk item
            }

            AnnounceCurrentBulkItem();
        }

        public static void NextCategory()
        {
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
            RefreshItems();
            if (categories.Count == 0) return;

            var currentItem = GetCurrentItem();
            if (currentItem == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            // Get the actual thing to jump to (considering bulk index)
            Thing targetThing = currentItem.Thing;
            if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
            {
                targetThing = currentItem.BulkThings[currentBulkIndex];
            }

            // Update map cursor position
            MapNavigationState.CurrentCursorPosition = targetThing.Position;

            // Jump camera to position
            Find.CameraDriver.JumpToCurrentMapLoc(targetThing.Position);

            TolkHelper.Speak($"Jumped to {currentItem.Label}", SpeechPriority.Normal);
        }

        public static void ReadDistanceAndDirection()
        {
            RefreshItems();
            if (categories.Count == 0) return;

            var currentItem = GetCurrentItem();
            if (currentItem == null)
            {
                TolkHelper.Speak("No item selected", SpeechPriority.High);
                return;
            }

            // Get the actual thing (considering bulk index)
            Thing targetThing = currentItem.Thing;
            IntVec3 targetPos = currentItem.Position;
            if (currentItem.IsBulkGroup && currentBulkIndex < currentItem.BulkCount)
            {
                targetThing = currentItem.BulkThings[currentBulkIndex];
                targetPos = targetThing.Position;
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

            var targetThing = item.BulkThings[currentBulkIndex];
            var cursorPos = MapNavigationState.CurrentCursorPosition;
            var distance = (targetThing.Position - cursorPos).LengthHorizontal;
            var position = currentBulkIndex + 1;

            TolkHelper.Speak($"{item.Label} - {distance:F1} tiles, {position} of {item.BulkCount}", SpeechPriority.Normal);
        }
    }
}
