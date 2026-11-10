using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class LineBoxTests
{
    private static LineBox Box(float x, float y, float width, float height)
    {
        return new LineBox { X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void OnLineOf_CentreWithinTheLineInclusive()
    {
        LineBox line = Box(0f, 100f, 200f, 24f);
        Assert.True(Box(300f, 90f, 20f, 20f).OnLineOf(line));
        Assert.True(Box(300f, 114f, 20f, 20f).OnLineOf(line));
        Assert.False(Box(300f, 116f, 20f, 20f).OnLineOf(line));
    }

    [Fact]
    public void Inside_AllowsEdgeSlack()
    {
        LineBox outer = Box(100f, 0f, 200f, 24f);
        Assert.True(Box(98f, 0f, 204f, 10f).Inside(outer));
        Assert.False(Box(97f, 0f, 50f, 10f).Inside(outer));
        Assert.False(Box(260f, 0f, 43f, 10f).Inside(outer));
    }

    [Fact]
    public void OverlapsColumn_IsStrict()
    {
        LineBox a = Box(100f, 0f, 100f, 10f);
        Assert.True(Box(199f, 50f, 10f, 10f).OverlapsColumn(a));
        Assert.False(Box(200f, 50f, 10f, 10f).OverlapsColumn(a));
        Assert.False(Box(90f, 50f, 10f, 10f).OverlapsColumn(a));
    }

    [Fact]
    public void Gaps_MeasureFromTheNearEdge()
    {
        LineBox a = Box(100f, 100f, 100f, 20f);
        Assert.Equal(5f, Box(100f, 125f, 10f, 10f).GapBelow(a));
        Assert.Equal(-4f, Box(196f, 100f, 10f, 10f).GapRight(a));
    }

    [Fact]
    public void Contains_ExcludesRightAndBottomEdges()
    {
        LineBox a = Box(0f, 0f, 10f, 10f);
        Assert.True(a.Contains(0f, 0f));
        Assert.False(a.Contains(10f, 5f));
        Assert.False(a.Contains(5f, 10f));
    }
}
