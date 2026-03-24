using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared helper for tabular menu navigation, sorting, and typeahead search.
    /// Used by AnimalsMenuState and WildlifeMenuState via composition.
    /// </summary>
    public class TabularMenuHelper<TItem>
    {
        // === State ===
        private int currentRowIndex = 0;
        private int currentColumnIndex = 0;
        private int sortColumnIndex = 0;
        private bool sortDescending = false;
        private readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // === Delegates for data access ===
        private readonly Func<int> getColumnCount;
        private readonly Func<TItem, string> getItemLabel;
        private readonly Func<int, string> getColumnName;
        private readonly Func<TItem, int, string> getColumnValue;
        private readonly Func<IList<TItem>, int, bool, IList<TItem>> sortByColumn;
        private readonly Func<TItem, int, string> getColumnTooltip;

        // === Properties ===
        public int CurrentRowIndex
        {
            get => currentRowIndex;
            set => currentRowIndex = value;
        }

        public int CurrentColumnIndex
        {
            get => currentColumnIndex;
            set => currentColumnIndex = value;
        }

        public int SortColumnIndex => sortColumnIndex;
        public bool SortDescending => sortDescending;
        public TypeaheadSearchHelper Typeahead => typeahead;

        // === Constructor ===

        /// <summary>
        /// Creates a new TabularMenuHelper with the specified data access delegates.
        /// </summary>
        /// <param name="getColumnCount">Returns the total number of columns</param>
        /// <param name="getItemLabel">Returns the display label for an item (used for announcements and search)</param>
        /// <param name="getColumnName">Returns the name of a column by index</param>
        /// <param name="getColumnValue">Returns the value of a column for an item</param>
        /// <param name="sortByColumn">Sorts items by column index and direction, returns new sorted list</param>
        /// <param name="defaultSortColumn">Initial sort column index</param>
        /// <param name="defaultSortDescending">Initial sort direction</param>
        /// <param name="getColumnTooltip">Optional: returns tooltip text for a column and item (shown only on column navigation)</param>
        public TabularMenuHelper(
            Func<int> getColumnCount,
            Func<TItem, string> getItemLabel,
            Func<int, string> getColumnName,
            Func<TItem, int, string> getColumnValue,
            Func<IList<TItem>, int, bool, IList<TItem>> sortByColumn,
            int defaultSortColumn = 0,
            bool defaultSortDescending = false,
            Func<TItem, int, string> getColumnTooltip = null)
        {
            this.getColumnCount = getColumnCount ?? throw new ArgumentNullException(nameof(getColumnCount));
            this.getItemLabel = getItemLabel ?? throw new ArgumentNullException(nameof(getItemLabel));
            this.getColumnName = getColumnName ?? throw new ArgumentNullException(nameof(getColumnName));
            this.getColumnValue = getColumnValue ?? throw new ArgumentNullException(nameof(getColumnValue));
            this.sortByColumn = sortByColumn ?? throw new ArgumentNullException(nameof(sortByColumn));
            this.sortColumnIndex = defaultSortColumn;
            this.sortDescending = defaultSortDescending;
            this.getColumnTooltip = getColumnTooltip;
        }

        // === Navigation Methods ===

        /// <summary>
        /// Moves to next row with wrap-around.
        /// </summary>
        /// <param name="itemCount">Total number of items</param>
        /// <returns>True if there are items to navigate</returns>
        public bool SelectNextRow(int itemCount)
        {
            if (itemCount == 0) return false;
            currentRowIndex = (currentRowIndex + 1) % itemCount;
            return true;
        }

        /// <summary>
        /// Moves to previous row with wrap-around.
        /// </summary>
        /// <param name="itemCount">Total number of items</param>
        /// <returns>True if there are items to navigate</returns>
        public bool SelectPreviousRow(int itemCount)
        {
            if (itemCount == 0) return false;
            currentRowIndex = (currentRowIndex - 1 + itemCount) % itemCount;
            return true;
        }

        /// <summary>
        /// Moves to next column with wrap-around.
        /// </summary>
        /// <returns>True always (columns are always available)</returns>
        public bool SelectNextColumn()
        {
            int totalColumns = getColumnCount();
            currentColumnIndex = (currentColumnIndex + 1) % totalColumns;
            return true;
        }

        /// <summary>
        /// Moves to previous column with wrap-around.
        /// </summary>
        /// <returns>True always (columns are always available)</returns>
        public bool SelectPreviousColumn()
        {
            int totalColumns = getColumnCount();
            currentColumnIndex = (currentColumnIndex - 1 + totalColumns) % totalColumns;
            return true;
        }

        /// <summary>
        /// Jumps to first row.
        /// </summary>
        /// <param name="itemCount">Total number of items</param>
        /// <returns>True if there are items</returns>
        public bool JumpToFirst(int itemCount)
        {
            if (itemCount == 0) return false;
            currentRowIndex = 0;
            typeahead.ClearSearch();
            return true;
        }

        /// <summary>
        /// Jumps to last row.
        /// </summary>
        /// <param name="itemCount">Total number of items</param>
        /// <returns>True if there are items</returns>
        public bool JumpToLast(int itemCount)
        {
            if (itemCount == 0) return false;
            currentRowIndex = itemCount - 1;
            typeahead.ClearSearch();
            return true;
        }

        // === Sorting Methods ===

        /// <summary>
        /// Toggles sort by current column. Returns sorted list and preserves selected item.
        /// </summary>
        /// <param name="items">Current item list</param>
        /// <param name="sortDirection">Output: sort direction string ("ascending"/"descending")</param>
        /// <returns>Newly sorted list</returns>
        public IList<TItem> ToggleSortByCurrentColumn(IList<TItem> items, out string sortDirection)
        {
            if (sortColumnIndex == currentColumnIndex)
            {
                // Same column - toggle direction
                sortDescending = !sortDescending;
            }
            else
            {
                // New column - sort ascending
                sortColumnIndex = currentColumnIndex;
                sortDescending = false;
            }

            sortDirection = sortDescending ? "descending" : "ascending";

            // Remember current item to preserve selection
            TItem currentItem = default(TItem);
            bool hasCurrentItem = false;
            if (currentRowIndex >= 0 && currentRowIndex < items.Count)
            {
                currentItem = items[currentRowIndex];
                hasCurrentItem = true;
            }

            // Sort
            var sortedItems = sortByColumn(items, sortColumnIndex, sortDescending);

            // Restore selection
            if (hasCurrentItem)
            {
                int newIndex = -1;
                for (int i = 0; i < sortedItems.Count; i++)
                {
                    if (EqualityComparer<TItem>.Default.Equals(sortedItems[i], currentItem))
                    {
                        newIndex = i;
                        break;
                    }
                }
                currentRowIndex = newIndex >= 0 ? newIndex : 0;
            }
            else
            {
                currentRowIndex = 0;
            }

            return sortedItems;
        }

        /// <summary>
        /// Gets the column name for the current sort column.
        /// </summary>
        public string GetSortColumnName()
        {
            return getColumnName(sortColumnIndex);
        }

        /// <summary>
        /// Gets the column name for the current column.
        /// </summary>
        public string GetCurrentColumnName()
        {
            return getColumnName(currentColumnIndex);
        }

        // === Typeahead Search Methods ===

        /// <summary>
        /// Handles character input for typeahead search.
        /// </summary>
        /// <param name="c">Character typed</param>
        /// <param name="items">Current item list</param>
        /// <param name="newIndex">Output: new index if match found</param>
        /// <returns>True if a match was found, false if no matches</returns>
        public bool HandleTypeahead(char c, IList<TItem> items, out int newIndex)
        {
            var labels = new List<string>(items.Count);
            foreach (var item in items)
            {
                labels.Add(getItemLabel(item));
            }

            if (typeahead.ProcessCharacterInput(c, labels, out newIndex))
            {
                if (newIndex >= 0)
                {
                    currentRowIndex = newIndex;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Handles backspace for typeahead search.
        /// </summary>
        /// <param name="items">Current item list</param>
        /// <param name="newIndex">Output: new index if search updated</param>
        /// <returns>True if search was active and updated</returns>
        public bool HandleBackspace(IList<TItem> items, out int newIndex)
        {
            if (!typeahead.HasActiveSearch)
            {
                newIndex = -1;
                return false;
            }

            var labels = new List<string>(items.Count);
            foreach (var item in items)
            {
                labels.Add(getItemLabel(item));
            }

            if (typeahead.ProcessBackspace(labels, out newIndex))
            {
                if (newIndex >= 0)
                {
                    currentRowIndex = newIndex;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets item labels for typeahead search.
        /// </summary>
        public List<string> GetItemLabels(IList<TItem> items)
        {
            var labels = new List<string>(items.Count);
            foreach (var item in items)
            {
                labels.Add(getItemLabel(item));
            }
            return labels;
        }

        // === Announcement Helpers ===

        /// <summary>
        /// Builds announcement text for current cell.
        /// </summary>
        /// <param name="item">Current item</param>
        /// <param name="itemCount">Total number of items</param>
        /// <param name="includeItemName">Whether to include item name in announcement</param>
        /// <returns>Formatted announcement string</returns>
        public string BuildCellAnnouncement(TItem item, int itemCount, bool includeItemName)
        {
            string columnName = getColumnName(currentColumnIndex);
            string columnValue = getColumnValue(item, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentRowIndex, itemCount);

            if (includeItemName)
            {
                string itemLabel = getItemLabel(item);
                // Avoid duplication if column value equals or starts with item label (e.g., Name column)
                if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
                {
                    // Just announce the column value (which may include activity)
                    string sep = columnValue.EndsWith(".") ? " " : ". ";
                    return $"{columnValue}{sep}{position}";
                }
                // Don't add period if value already ends with one
                string separator = columnValue.EndsWith(".") ? " " : ". ";
                return $"{itemLabel} - {columnName}: {columnValue}{separator}{position}";
            }
            else
            {
                string result = $"{columnName}: {columnValue}";
                string tooltip = getColumnTooltip?.Invoke(item, currentColumnIndex);
                if (!string.IsNullOrEmpty(tooltip))
                    result += ". " + tooltip;
                return result;
            }
        }

        /// <summary>
        /// Builds announcement text for current cell with search context.
        /// </summary>
        /// <param name="item">Current item</param>
        /// <param name="itemCount">Total number of items</param>
        /// <returns>Formatted announcement string with search context if active</returns>
        public string BuildCellAnnouncementWithSearch(TItem item, int itemCount)
        {
            string columnName = getColumnName(currentColumnIndex);
            string columnValue = getColumnValue(item, currentColumnIndex);
            string itemLabel = getItemLabel(item);
            string position = MenuHelper.FormatPosition(currentRowIndex, itemCount);

            // Avoid duplication if column value equals or starts with item label (e.g., Name column)
            string announcement;
            if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
            {
                // Just announce the column value (which may include activity)
                string sep = columnValue.EndsWith(".") ? " " : ". ";
                announcement = $"{columnValue}{sep}{position}";
            }
            else
            {
                // Don't add period if value already ends with one
                string separator = columnValue.EndsWith(".") ? " " : ". ";
                announcement = $"{itemLabel} - {columnName}: {columnValue}{separator}{position}";
            }

            if (typeahead.HasActiveSearch)
            {
                announcement += $", match {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} for '{typeahead.SearchBuffer}'";
            }

            return announcement;
        }

        // === Lifecycle Methods ===

        /// <summary>
        /// Resets helper state for menu open.
        /// </summary>
        /// <param name="defaultSortColumn">Sort column to use (-1 to keep current)</param>
        /// <param name="defaultSortDescending">Sort direction to use</param>
        public void Reset(int defaultSortColumn = -1, bool defaultSortDescending = false)
        {
            currentRowIndex = 0;
            currentColumnIndex = 0;
            if (defaultSortColumn >= 0)
            {
                sortColumnIndex = defaultSortColumn;
            }
            sortDescending = defaultSortDescending;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Clears typeahead search state.
        /// </summary>
        public void ClearSearch()
        {
            typeahead.ClearSearch();
        }
    }
}
