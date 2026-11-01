using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TableModelTests
{
    [Fact]
    public void Columns_WrapOnlyWhenAsked()
    {
        var table = new TableModel(new ListModel(3), columnCount: 2, wrapColumns: false);

        Assert.Equal(MoveKind.Moved, table.NextColumn().Kind);
        Assert.Equal(1, table.ColumnIndex);
        Assert.Equal(MoveKind.AtEdge, table.NextColumn().Kind);
        Assert.Equal(1, table.ColumnIndex);

        table.WrapColumns = true;
        Assert.Equal(MoveKind.Wrapped, table.NextColumn().Kind);
        Assert.Equal(0, table.ColumnIndex);
        Assert.Equal(MoveKind.Wrapped, table.PreviousColumn().Kind);
        Assert.Equal(1, table.ColumnIndex);
    }

    [Fact]
    public void Rows_AreTheSharedListModel()
    {
        var rows = new ListModel(3);
        var table = new TableModel(rows, 2);

        rows.MoveNext();
        Assert.Same(rows, table.Rows);
        Assert.Equal(1, table.Rows.Index);
    }

    [Fact]
    public void ColumnPosition_IsOneBased()
    {
        var table = new TableModel(new ListModel(2), 3);
        Assert.Equal(1, table.ColumnPosition);
        table.NextColumn();
        Assert.Equal(2, table.ColumnPosition);
    }

    [Fact]
    public void SortCycle_DescendingThenAscendingThenCleared()
    {
        var table = new TableModel(new ListModel(5), 3);
        table.NextColumn(); // column 1

        Assert.Equal(SortCycleResult.SortedDescending, table.ToggleSortCycle(true));
        Assert.Equal(1, table.SortColumnIndex);
        Assert.True(table.SortDescending);
        Assert.True(table.HasActiveSort);

        Assert.Equal(SortCycleResult.SortedAscending, table.ToggleSortCycle(true));
        Assert.False(table.SortDescending);

        Assert.Equal(SortCycleResult.Cleared, table.ToggleSortCycle(true));
        Assert.False(table.HasActiveSort);
        Assert.Equal(-1, table.SortColumnIndex);
    }

    [Fact]
    public void SortCycle_NewColumnRestartsAtDescending()
    {
        var table = new TableModel(new ListModel(5), 3);
        table.ToggleSortCycle(true); // column 0 descending

        table.NextColumn();
        Assert.Equal(SortCycleResult.SortedDescending, table.ToggleSortCycle(true));
        Assert.Equal(1, table.SortColumnIndex);
    }

    [Fact]
    public void SetSort_AdoptedStateDrivesTheNextCycleStep()
    {
        var table = new TableModel(new ListModel(5), 3);
        table.NextColumn();

        table.SetSort(1, descending: true);
        Assert.Equal(1, table.SortColumnIndex);
        Assert.True(table.SortDescending);
        Assert.Equal(SortCycleResult.SortedAscending, table.ToggleSortCycle(true));

        table.SetSort(-1, descending: true);
        Assert.False(table.HasActiveSort);
        Assert.False(table.SortDescending);

        table.SetSort(3, descending: true);
        Assert.False(table.HasActiveSort);
    }

    [Fact]
    public void SortCycle_UnsortableColumnRefusesAndKeepsState()
    {
        var table = new TableModel(new ListModel(5), 2);
        table.ToggleSortCycle(true);

        table.NextColumn();
        Assert.Equal(SortCycleResult.NotSortable, table.ToggleSortCycle(false));
        Assert.Equal(0, table.SortColumnIndex); // prior sort untouched
    }

    [Fact]
    public void SetColumnCount_ClampsCursorAndResetsOrphanedSort()
    {
        var table = new TableModel(new ListModel(5), 4);
        table.MoveToColumn(3);
        table.ToggleSortCycle(true); // sorted by column 3

        table.SetColumnCount(2);
        Assert.Equal(1, table.ColumnIndex);
        Assert.False(table.HasActiveSort); // sort column no longer exists

        table.SetColumnCount(2);
        Assert.Equal(1, table.ColumnIndex); // idempotent refresh keeps the cursor
    }

    [Fact]
    public void MoveToColumn_JumpAndValidate()
    {
        var table = new TableModel(new ListModel(5), 3);
        Assert.Equal(MoveKind.Moved, table.MoveToColumn(2).Kind);
        Assert.Equal(MoveKind.AtEdge, table.MoveToColumn(2).Kind);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => table.MoveToColumn(3));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new TableModel(new ListModel(1), 0));
    }
}
