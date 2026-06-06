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
        private int sortColumnIndex = -1; // -1 = no active sort (default order)
        private bool sortDescending = false;
        private readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private IList<TItem> defaultOrder; // Captured at menu open for "clear sort" restoration

        // === Delegates for data access ===
        private readonly Func<int> getColumnCount;
        private readonly Func<TItem, string> getItemLabel;
        private readonly Func<int, string> getColumnName;
        private readonly Func<TItem, int, string> getColumnValue;
        private readonly Func<IList<TItem>, int, bool, IList<TItem>> sortByColumn;
        private readonly Func<TItem, int, string> getColumnTooltip;
        private readonly Func<int, bool> isColumnSortable;

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
        public bool HasActiveSort => sortColumnIndex >= 0;
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
        /// <param name="getColumnTooltip">Optional: returns tooltip text for a column and item (shown only on column navigation)</param>
        /// <param name="isColumnSortable">Optional: returns whether a column supports sorting (default: all columns sortable)</param>
        public TabularMenuHelper(
            Func<int> getColumnCount,
            Func<TItem, string> getItemLabel,
            Func<int, string> getColumnName,
            Func<TItem, int, string> getColumnValue,
            Func<IList<TItem>, int, bool, IList<TItem>> sortByColumn,
            Func<TItem, int, string> getColumnTooltip = null,
            Func<int, bool> isColumnSortable = null)
        {
            this.getColumnCount = getColumnCount ?? throw new ArgumentNullException(nameof(getColumnCount));
            this.getItemLabel = getItemLabel ?? throw new ArgumentNullException(nameof(getItemLabel));
            this.getColumnName = getColumnName ?? throw new ArgumentNullException(nameof(getColumnName));
            this.getColumnValue = getColumnValue ?? throw new ArgumentNullException(nameof(getColumnValue));
            this.sortByColumn = sortByColumn ?? throw new ArgumentNullException(nameof(sortByColumn));
            this.getColumnTooltip = getColumnTooltip;
            this.isColumnSortable = isColumnSortable ?? (col => true);
            // sortColumnIndex starts at -1 (no active sort); default sort is applied by the caller
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
            // While a typeahead search is active, step through the matching rows rather
            // than the full list, so Down advances "match X of N" instead of leaking to the
            // row beneath. GetNextMatch wraps and keeps CurrentMatchPosition in sync.
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                currentRowIndex = typeahead.GetNextMatch(currentRowIndex);
                return true;
            }
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
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                currentRowIndex = typeahead.GetPreviousMatch(currentRowIndex);
                return true;
            }
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
            // During an active search, Home goes to the first match and keeps the search,
            // rather than clearing it and jumping out to the top of the full list.
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                currentRowIndex = typeahead.GetFirstMatch();
                return true;
            }
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
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                currentRowIndex = typeahead.GetLastMatch();
                return true;
            }
            currentRowIndex = itemCount - 1;
            typeahead.ClearSearch();
            return true;
        }

        // === Sorting Methods ===

        /// <summary>
        /// Captures the default item order for "clear sort" restoration.
        /// Must be called after the initial default sort during menu Open().
        /// </summary>
        public void SetDefaultOrder(IList<TItem> items)
        {
            defaultOrder = new List<TItem>(items);
        }

        /// <summary>
        /// Toggles sort by current column using a 3-state cycle matching vanilla RimWorld:
        /// no sort -> descending -> ascending -> clear (back to default order).
        /// Returns null if the column is not sortable.
        /// </summary>
        /// <param name="items">Current item list</param>
        /// <param name="sortDirection">Output: "descending", "ascending", or null if sort was cleared or column not sortable</param>
        /// <param name="sortCleared">Output: true if sort was cleared (returned to default order)</param>
        /// <returns>Newly sorted list, or null if column is not sortable</returns>
        public IList<TItem> ToggleSortByCurrentColumn(IList<TItem> items, out string sortDirection, out bool sortCleared)
        {
            sortCleared = false;

            // Check if column supports sorting
            if (!isColumnSortable(currentColumnIndex))
            {
                sortDirection = null;
                return null;
            }

            if (sortColumnIndex == currentColumnIndex)
            {
                // Same column - advance through 3-state cycle
                if (sortDescending)
                {
                    // descending -> ascending
                    sortDescending = false;
                }
                else
                {
                    // ascending -> clear sort (return to default order)
                    sortColumnIndex = -1;
                    sortDescending = false;
                    sortCleared = true;
                    sortDirection = null;

                    // Restore default order
                    if (defaultOrder != null)
                    {
                        var restored = new List<TItem>(defaultOrder);
                        RestoreSelection(items, restored);
                        return restored;
                    }
                    return items;
                }
            }
            else
            {
                // New column - start with descending (matching vanilla left-click)
                sortColumnIndex = currentColumnIndex;
                sortDescending = true;
            }

            sortDirection = sortDescending ? "descending" : "ascending";

            // Remember current item to preserve selection
            TItem currentItem = default(TItem);
            bool hasCurrentItem = currentRowIndex >= 0 && currentRowIndex < items.Count;
            if (hasCurrentItem)
            {
                currentItem = items[currentRowIndex];
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
        /// Re-sorts the list if currently sorted by the column being edited (matches vanilla behavior).
        /// Keeps the cursor at the same index position so the user lands on the "next" item.
        /// Returns the re-sorted list, or null if no resort was needed.
        /// </summary>
        public IList<TItem> ResortAfterEdit(IList<TItem> items)
        {
            // Only resort if actively sorted by the column we're currently on
            // Matches vanilla's check: table.SortingBy == def
            if (sortColumnIndex < 0 || sortColumnIndex != currentColumnIndex)
                return null;

            var sorted = sortByColumn(items, sortColumnIndex, sortDescending);
            // Keep same index (don't follow the edited item to its new position)
            if (currentRowIndex >= sorted.Count)
                currentRowIndex = sorted.Count - 1;
            return sorted;
        }

        private void RestoreSelection(IList<TItem> currentItems, IList<TItem> restoredItems)
        {
            if (currentRowIndex >= 0 && currentRowIndex < currentItems.Count)
            {
                TItem currentItem = currentItems[currentRowIndex];
                for (int i = 0; i < restoredItems.Count; i++)
                {
                    if (EqualityComparer<TItem>.Default.Equals(restoredItems[i], currentItem))
                    {
                        currentRowIndex = i;
                        return;
                    }
                }
            }
            currentRowIndex = 0;
        }

        /// <summary>
        /// Gets the column name for the current sort column, or null if no active sort.
        /// </summary>
        public string GetSortColumnName()
        {
            return sortColumnIndex >= 0 ? getColumnName(sortColumnIndex) : null;
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
        /// Callers opt out of naming the unchanged axis so screen reader users
        /// only hear what changed:
        ///   - Row nav (same column): pass includeColumnName=false.
        ///   - Column nav (same row): pass includeItemName=false.
        ///   - Initial entry / after sort / re-orientation: pass both true.
        /// The column tooltip is only appended on pure column change
        /// (includeColumnName=true && includeItemName=false), matching the
        /// "only announce changed context" rule.
        /// </summary>
        public string BuildCellAnnouncement(TItem item, int itemCount, bool includeItemName, bool includeColumnName = true)
        {
            string columnValue = getColumnValue(item, currentColumnIndex);
            string position = MenuHelper.FormatPosition(currentRowIndex, itemCount);
            string separator = columnValue.EndsWith(".") ? " " : ". ";

            // Dedup: on the Name column the cell value IS the item label, so
            // prefixing it with the item label would repeat ("Rosario: Rosario").
            if (includeItemName)
            {
                string itemLabel = getItemLabel(item);
                if (itemLabel == columnValue || columnValue.StartsWith(itemLabel))
                {
                    return $"{columnValue}{separator}{position}";
                }

                string columnName = includeColumnName ? getColumnName(currentColumnIndex) : null;
                return includeColumnName
                    ? $"{itemLabel} - {columnName}: {columnValue}{separator}{position}"
                    : $"{itemLabel}: {columnValue}{separator}{position}";
            }

            if (includeColumnName)
            {
                string columnName = getColumnName(currentColumnIndex);
                string result = $"{columnName}: {columnValue}";
                string tooltip = getColumnTooltip?.Invoke(item, currentColumnIndex);
                if (!string.IsNullOrEmpty(tooltip))
                    result += ". " + tooltip;
                return result;
            }

            // Neither axis — bare value + position (rare fallback).
            return $"{columnValue}{separator}{position}";
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

            announcement += typeahead.BuildSearchContextSuffix();

            return announcement;
        }

        // === Lifecycle Methods ===

        /// <summary>
        /// Resets helper state for menu open.
        /// </summary>
        public void Reset()
        {
            currentRowIndex = 0;
            currentColumnIndex = 0;
            sortColumnIndex = -1; // No active sort
            sortDescending = false;
            defaultOrder = null;
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
