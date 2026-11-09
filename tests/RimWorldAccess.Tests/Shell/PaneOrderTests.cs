using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="PaneOrder"/>, the geometry-only rule that groups presentation rows by
/// their capture clip so a multi-pane window (Colony Manager Redux's job list / settings /
/// allowed-animals columns) reads pane by pane instead of zipper-interleaved by raw Y-then-X.
/// </summary>
public class PaneOrderTests
{
    private static PaneOrderRow Row(int clip, float x, float y)
    {
        return new PaneOrderRow { ClipId = clip, X = x, Y = y };
    }

    [Fact]
    public void ThreePanes_RankedTopToBottomThenLeftToRight()
    {
        // Three side-by-side panes whose topmost rows all start at the same Y: clip 1 (x=18) is
        // the job list, clip 2 (x=342) is the settings pane, clip 3 (x=743) is the
        // allowed-animals column — left to right, exactly as a sighted player reads them.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 1, x: 18f, y: 68f),
            Row(clip: 2, x: 342f, y: 68f),
            Row(clip: 3, x: 743f, y: 68f),
            Row(clip: 2, x: 342f, y: 136f),
            Row(clip: 3, x: 743f, y: 115f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]); // clip 1, leftmost pane
        Assert.Equal(1, ranks[1]); // clip 2
        Assert.Equal(1, ranks[3]);
        Assert.Equal(2, ranks[2]); // clip 3, rightmost pane
        Assert.Equal(2, ranks[4]);
    }

    [Fact]
    public void ThreePanes_WithDifferingTopmostY_RanksByTheEarliestRowInEachPane()
    {
        // The real dump geometry (S1 blueprint excerpt): the allowed-animals pane's first
        // captured row (y=55) actually starts slightly ABOVE the job list's (y=68) and the
        // settings pane's (y=74) — panes are ranked by their own minimum Y, not by column order,
        // so the animals pane legitimately sorts first here.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 1, x: 18f, y: 68f),
            Row(clip: 2, x: 342f, y: 74f),
            Row(clip: 3, x: 743f, y: 55f),
            Row(clip: 2, x: 342f, y: 136f),
            Row(clip: 3, x: 743f, y: 115f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(1, ranks[0]); // clip 1
        Assert.Equal(2, ranks[1]); // clip 2
        Assert.Equal(2, ranks[3]);
        Assert.Equal(0, ranks[2]); // clip 3 -- its earliest row (y=55) is the topmost of all
        Assert.Equal(0, ranks[4]);
    }

    [Fact]
    public void SinglePane_EveryRowRanksTheSame()
    {
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 7, x: 0f, y: 0f),
            Row(clip: 7, x: 40f, y: 60f),
            Row(clip: 7, x: 10f, y: 120f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.All(ranks, rank => Assert.Equal(0, rank));
    }

    [Fact]
    public void PanesSharingTheirTopmostY_TieBrokenByX()
    {
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 2, x: 400f, y: 20f),
            Row(clip: 1, x: 0f, y: 20f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(1, ranks[0]); // clip 2 sits to the right
        Assert.Equal(0, ranks[1]); // clip 1 sits to the left, wins the tie
    }

    [Fact]
    public void NoClipInfo_EveryRowSharesOneImplicitClip_FallsBackToCurrentBehavior()
    {
        // A caller with no clip metadata at all assigns every row the same ClipId (e.g. 0) —
        // this must degenerate to rank 0 for everyone, so a caller's own secondary sort is the
        // only thing that ever moves a row, exactly like before pane grouping existed.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 0, x: 50f, y: 10f),
            Row(clip: 0, x: 5f, y: 90f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]);
        Assert.Equal(0, ranks[1]);
    }

    [Fact]
    public void EmptyInput_ReturnsNoRanks()
    {
        Assert.Empty(PaneOrder.ComputePaneRanks(new List<PaneOrderRow>()));
        Assert.Empty(PaneOrder.ComputePaneRanks(null));
    }

    private static PaneOrderRow Scrolled(int clip, int container, float x, float xMax, float y, float height = 22f)
    {
        return new PaneOrderRow { ClipId = clip, ContainerId = container, X = x, XMax = xMax, Y = y, YMax = y + height };
    }

    private static PaneOrderRow Chrome(int clip, float x, float xMax, float y, float height)
    {
        return new PaneOrderRow { ClipId = clip, X = x, XMax = xMax, Y = y, YMax = y + height };
    }

    [Fact]
    public void CollapsibleSectionsNestedInOneScrollView_ShareOnePane_ChromeOutsideStaysApart()
    {
        var rows = new List<PaneOrderRow>
        {
            Chrome(clip: 0, x: 208f, xMax: 905f, y: 68f, height: 35f),
            Scrolled(clip: 1, container: 0, x: 208f, xMax: 1052f, y: 161f),
            Scrolled(clip: 1, container: 0, x: 208f, xMax: 1052f, y: 183f),
            Scrolled(clip: 2, container: 0, x: 208f, xMax: 1052f, y: 242f),
            Scrolled(clip: 3, container: 0, x: 208f, xMax: 1052f, y: 280f),
            Scrolled(clip: 2, container: 0, x: 208f, xMax: 1052f, y: 712f),
            Scrolled(clip: 1, container: 0, x: 208f, xMax: 1052f, y: 978f),
            Chrome(clip: 4, x: 580f, xMax: 700f, y: 695f, height: 40f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]);
        for (int i = 1; i <= 6; i++)
        {
            Assert.Equal(1, ranks[i]);
        }
        Assert.Equal(2, ranks[7]);
    }

    [Fact]
    public void SideBySideGroupsInOneScrollView_StayApart()
    {
        var rows = new List<PaneOrderRow>
        {
            Scrolled(clip: 1, container: 0, x: 0f, xMax: 800f, y: 0f),
            Scrolled(clip: 2, container: 0, x: 0f, xMax: 390f, y: 40f),
            Scrolled(clip: 3, container: 0, x: 410f, xMax: 800f, y: 40f),
            Scrolled(clip: 2, container: 0, x: 0f, xMax: 390f, y: 80f),
            Scrolled(clip: 3, container: 0, x: 410f, xMax: 800f, y: 80f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(ranks[0], ranks[1]);
        Assert.NotEqual(ranks[1], ranks[2]);
        Assert.Equal(ranks[1], ranks[3]);
        Assert.Equal(ranks[2], ranks[4]);
        Assert.True(ranks[1] < ranks[2]);
    }

    [Fact]
    public void StackedGroupsInDifferentScrollViews_StayApart()
    {
        var rows = new List<PaneOrderRow>
        {
            Scrolled(clip: 1, container: 0, x: 0f, xMax: 400f, y: 100f),
            Scrolled(clip: 2, container: 1, x: 0f, xMax: 400f, y: 50f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(1, ranks[0]);
        Assert.Equal(0, ranks[1]);
    }

    [Fact]
    public void StackedGroupsOutsideAnyScrollView_StayApart()
    {
        var rows = new List<PaneOrderRow>
        {
            Chrome(clip: 1, x: 0f, xMax: 800f, y: 0f, height: 35f),
            Chrome(clip: 2, x: 340f, xMax: 460f, y: 600f, height: 40f),
            Chrome(clip: 1, x: 0f, xMax: 800f, y: 300f, height: 22f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]);
        Assert.Equal(1, ranks[1]);
        Assert.Equal(0, ranks[2]);
    }
}
