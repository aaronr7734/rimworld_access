using System;

namespace RimWorldAccess.Shell
{
    /// <summary>What ToggleSortCycle did, so the scope can speak and re-order its items.</summary>
    public enum SortCycleResult
    {
        /// <summary>The current column does not support sorting.</summary>
        NotSortable,
        /// <summary>Now sorted by the current column, descending (vanilla's first click).</summary>
        SortedDescending,
        /// <summary>Now sorted by the current column, ascending.</summary>
        SortedAscending,
        /// <summary>Sort cleared; caller restores its default item order.</summary>
        Cleared,
    }

    /// <summary>
    /// The grid state a table region adds on top of its row cursor: a column
    /// cursor and the 3-state sort cycle (none → descending → ascending →
    /// cleared, like vanilla column clicks). Rows are NOT duplicated here —
    /// <see cref="Rows"/> is the same ListModel the region already owns, so
    /// the row cursor has exactly one home whether the region is read as a
    /// list or as a table. Items and their ordering stay with the owning
    /// scope (sorting uses the game's own comparers — never display strings);
    /// the model only tracks positions and the cycle.
    ///
    /// Wrap policy: <see cref="WrapColumns"/> mirrors the same setting the
    /// row cursor uses, so both axes stop-or-wrap consistently and the scope
    /// speaks the canonical edge phrase on AtEdge.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TableModel
    {
        /// <summary>The region's row cursor (row 0 is the header row by scope convention).</summary>
        public readonly ListModel Rows;

        /// <summary>Wrap from last to first column and vice versa (the WrapNavigation setting).</summary>
        public bool WrapColumns;

        private int columnCount;
        private int columnIndex;
        private int sortColumnIndex = -1;
        private bool sortDescending;

        public TableModel(ListModel rows, int columnCount, bool wrapColumns = false)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (columnCount < 1)
                throw new ArgumentOutOfRangeException(nameof(columnCount), "A table needs at least one column.");
            Rows = rows;
            this.columnCount = columnCount;
            WrapColumns = wrapColumns;
        }

        public int ColumnCount
        {
            get { return columnCount; }
        }

        public int ColumnIndex
        {
            get { return columnIndex; }
        }

        /// <summary>1-based column position for announcements ("column 2 of 8").</summary>
        public int ColumnPosition
        {
            get { return columnIndex + 1; }
        }

        /// <summary>Sorted column, -1 when unsorted.</summary>
        public int SortColumnIndex
        {
            get { return sortColumnIndex; }
        }

        public bool SortDescending
        {
            get { return sortDescending; }
        }

        public bool HasActiveSort
        {
            get { return sortColumnIndex >= 0; }
        }

        /// <summary>Column set changed; the column cursor clamps and an out-of-range sort resets.</summary>
        public void SetColumnCount(int newCount)
        {
            if (newCount < 1)
                throw new ArgumentOutOfRangeException(nameof(newCount));
            columnCount = newCount;
            if (columnIndex >= columnCount)
                columnIndex = columnCount - 1;
            if (sortColumnIndex >= columnCount)
            {
                sortColumnIndex = -1;
                sortDescending = false;
            }
        }

        public void SetSort(int column, bool descending)
        {
            bool valid = column >= 0 && column < columnCount;
            sortColumnIndex = valid ? column : -1;
            sortDescending = valid && descending;
        }

        public MoveResult NextColumn()
        {
            return MoveColumnBy(1);
        }

        public MoveResult PreviousColumn()
        {
            return MoveColumnBy(-1);
        }

        private MoveResult MoveColumnBy(int delta)
        {
            int target = columnIndex + delta;
            if (target >= columnCount)
            {
                if (WrapColumns)
                {
                    columnIndex = 0;
                    return new MoveResult(MoveKind.Wrapped, columnIndex);
                }
                return new MoveResult(MoveKind.AtEdge, columnIndex);
            }
            if (target < 0)
            {
                if (WrapColumns)
                {
                    columnIndex = columnCount - 1;
                    return new MoveResult(MoveKind.Wrapped, columnIndex);
                }
                return new MoveResult(MoveKind.AtEdge, columnIndex);
            }
            columnIndex = target;
            return new MoveResult(MoveKind.Moved, columnIndex);
        }

        /// <summary>Absolute column jump (typeahead match, per-screen column hotkeys).</summary>
        public MoveResult MoveToColumn(int target)
        {
            if (target < 0 || target >= columnCount)
                throw new ArgumentOutOfRangeException(nameof(target));
            if (target == columnIndex)
                return new MoveResult(MoveKind.AtEdge, columnIndex);
            columnIndex = target;
            return new MoveResult(MoveKind.Moved, columnIndex);
        }

        /// <summary>
        /// Advances the vanilla 3-state sort cycle on the current column:
        /// unsorted → descending → ascending → cleared. The caller re-orders
        /// its items on the result and repositions the row cursor to keep the
        /// selection.
        /// </summary>
        public SortCycleResult ToggleSortCycle(bool currentColumnSortable)
        {
            if (!currentColumnSortable)
                return SortCycleResult.NotSortable;

            if (sortColumnIndex == columnIndex)
            {
                if (sortDescending)
                {
                    sortDescending = false;
                    return SortCycleResult.SortedAscending;
                }
                sortColumnIndex = -1;
                sortDescending = false;
                return SortCycleResult.Cleared;
            }

            sortColumnIndex = columnIndex;
            sortDescending = true;
            return SortCycleResult.SortedDescending;
        }
    }
}
