using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TipIndexTests
{
    private static TipRect Square(float x, float y, float size = 10f) => new(x, y, size, size);

    // Shared clip for tests that don't care about clip-context matching —
    // every Add call in this file used to omit clip entirely; giving them all
    // the SAME clip preserves their original screen-rect-only intent.
    private static readonly TipClip DefaultClip = new(Square(0, 0, 0), 0);

    [Fact]
    public void QueryAt_EmptyIndex_ReturnsNull()
    {
        var index = new TipIndex();
        Assert.Null(index.QueryAt(Square(0, 0)));
    }

    [Fact]
    public void QueryAt_NoOverlap_ReturnsNull()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "tip");
        Assert.Null(index.QueryAt(Square(100, 100)));
    }

    [Fact]
    public void QueryAt_OverlappingTip_ResolvesText()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "Chop wood");
        Assert.Equal("Chop wood", index.QueryAt(Square(5, 5)));
    }

    [Fact]
    public void QueryAt_EdgeTouchingRects_DoNotMatch()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0, 10f), Square(0, 0, 10f), DefaultClip, 1, 0, () => "tip");
        // Shares only the x=10 edge.
        Assert.Null(index.QueryAt(Square(10, 0)));
    }

    [Fact]
    public void QueryAt_StackedTips_JoinInPriorityOrder_HighestFirst()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "low");
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 5, () => "high");
        Assert.Equal("high. low", index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void QueryAt_EqualPriority_KeepsRegistrationOrder()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "first");
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 0, () => "second");
        Assert.Equal("first. second", index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void Add_SameUniqueId_ReplacesEarlierEntry()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 7, 0, () => "stale");
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 7, 0, () => "fresh");
        Assert.Equal(1, index.Count);
        Assert.Equal("fresh", index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void QueryAt_EmptyResolvedText_IsSkipped()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 5, () => "");
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 0, () => "kept");
        Assert.Equal("kept", index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void QueryAt_AllResolversEmpty_ReturnsNull()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "");
        Assert.Null(index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void QueryAt_ThrowingResolver_IsSkipped_OthersSurvive()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 5, () => throw new InvalidOperationException("hover-time state"));
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 0, () => "survivor");
        Assert.Equal("survivor", index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void Add_NullResolver_IsIgnored()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, null!);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Clear_EmptiesIndex()
    {
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "tip");
        index.Clear();
        Assert.Equal(0, index.Count);
        Assert.Null(index.QueryAt(Square(2, 2)));
    }

    [Fact]
    public void QueryAtScreen_MatchesOnScreenRect_EvenWhenLocalRectsDiffer()
    {
        // The defect-4 scenario: a dialog title registers at local (0,0) and
        // a checkbox inside an unrelated ScrollView ALSO registers at local
        // (0,0), but their screen rects differ.
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(500, 500), DefaultClip, 1, 0, () => "title tip");
        Assert.Null(index.QueryAtScreen(Square(0, 0), DefaultClip));
        Assert.Equal("title tip", index.QueryAtScreen(Square(502, 502), DefaultClip));
    }

    [Fact]
    public void QueryAtScreen_DoesNotMatchOnLocalOverlapAlone()
    {
        // Two entries share a local rect (both restart at local (0,0)) but
        // occupy different screen rects — QueryAtScreen must not conflate them.
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "window title");
        index.Add(Square(0, 0), Square(200, 200), DefaultClip, 2, 0, () => "scrolled checkbox");
        Assert.Equal("window title", index.QueryAtScreen(Square(2, 2), DefaultClip));
        Assert.Equal("scrolled checkbox", index.QueryAtScreen(Square(202, 202), DefaultClip));
    }

    [Fact]
    public void QueryAtScreen_SameScreenRect_DifferentClip_DoesNotMatch()
    {
        // The below-the-fold residual hole: a row scrolled out of view can
        // unclip to the SAME screen rect a widget in an unrelated clip
        // context occupies (e.g. a dialog's Close button). Context equality
        // must be required in addition to rect overlap, or the Close button
        // would steal this off-screen row's tooltip.
        var index = new TipIndex();
        var scrolledOutClip = new TipClip(Square(0, 0, 0), 1);
        var closeButtonClip = new TipClip(Square(0, 0, 0), 0);
        index.Add(Square(0, 0), Square(50, 900), scrolledOutClip, 1, 0, () => "scrolled Advanced row");

        Assert.Null(index.QueryAtScreen(Square(50, 900), closeButtonClip));
    }

    [Fact]
    public void QueryAtScreen_SameClip_OverlappingScreenRect_StillMatches()
    {
        // The off-screen row must keep matching its OWN tooltip when queried
        // with its own clip context, even though its screen rect is off
        // screen — context equality gates cross-context collisions, not
        // legitimate same-context matches.
        var index = new TipIndex();
        var clip = new TipClip(Square(0, 0, 0), 1);
        index.Add(Square(0, 0), Square(50, 900), clip, 1, 0, () => "scrolled Advanced row");

        Assert.Equal("scrolled Advanced row", index.QueryAtScreen(Square(52, 902), clip));
    }

    /// <summary>
    /// S3 follow-up (rail-tip clip mismatch): two live reads of the
    /// IDENTICAL GUIClip context — a group-nested icon button's own row,
    /// then the caller's own follow-up TipRegion on the same rect (Colony
    /// Manager Redux's manager-tab rail) — can differ by a hair of float
    /// rounding under a non-identity UI matrix. TipClip.Equals must treat
    /// that as the SAME context, or the tip silently never resolves.
    /// </summary>
    [Fact]
    public void TipClip_Equals_ToleratesSubPixelFloatJitter()
    {
        var a = new TipClip(new TipRect(517.00001f, 200f, 34f, 34f), 2);
        var b = new TipClip(new TipRect(517f, 200f, 34f, 34f), 2);
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
    }

    [Fact]
    public void TipClip_Equals_StillRejectsRealDifferences()
    {
        // A genuinely different clip (a different row, a different column)
        // differs by at least a real pixel in practice — the tolerance must
        // not swallow that, or QueryAtScreen's whole cross-context
        // protection (see the below-the-fold tests above) would erode.
        var a = new TipClip(new TipRect(517f, 200f, 34f, 34f), 2);
        var b = new TipClip(new TipRect(518f, 200f, 34f, 34f), 2);
        Assert.False(a.Equals(b));

        var sameRectDifferentDepth = new TipClip(new TipRect(517f, 200f, 34f, 34f), 3);
        Assert.False(a.Equals(sameRectDifferentDepth));
    }

    [Fact]
    public void QueryAtScreen_SubPixelClipJitter_StillMatches()
    {
        // Integration-level twin of TipClip_Equals_ToleratesSubPixelFloatJitter:
        // the registration's clip and the query's clip differ by a hair —
        // exactly what two separate GuiSpace.CurrentClip() reads of the same
        // live context can produce — and the tip must still resolve.
        var index = new TipIndex();
        var registeredClip = new TipClip(new TipRect(0f, 0f, 200f, 40f), 1);
        var queryClip = new TipClip(new TipRect(0.00001f, 0f, 200f, 40f), 1);
        index.Add(Square(0, 0), Square(932, 155, 48f), registeredClip, 1, 0, () => "Tick to enable");

        Assert.Equal("Tick to enable", index.QueryAtScreen(Square(932, 155, 48f), queryClip));
    }

    /// <summary>
    /// S3 tooltip-bleed fix: Dubs Bad Hygiene's Scrollerball/RowYourBoat draw
    /// narrow adjacent columns whose TipRegion rects sit close enough that a
    /// neighbour's tip numerically overlaps this widget by a sliver (live-
    /// observed: "VPE_SkeletalBody. Corpse_VVE_Wisent"). QueryAtScreen must
    /// prefer the entry that actually CONTAINS the widget's center over one
    /// that merely clips its edge.
    /// </summary>
    [Fact]
    public void QueryAt_OnlyHitIsANeighboursOnePixelBleed_ReturnsNull()
    {
        // Float menu rows overlap by one pixel; a row with no tip must not borrow the row above's.
        var index = new TipIndex();
        index.Add(Square(0, 0), new TipRect(0, 0, 100, 30), DefaultClip, 1, 0, () => "row above tip");
        Assert.Null(index.QueryAt(new TipRect(0, 29, 100, 30)));
    }

    [Fact]
    public void QueryAt_SubRectTip_StillMatches()
    {
        // An icon's tip covers only part of its row and still belongs to it.
        var index = new TipIndex();
        index.Add(Square(0, 0), new TipRect(0, 0, 24, 24), DefaultClip, 1, 0, () => "icon tip");
        Assert.Equal("icon tip", index.QueryAt(new TipRect(0, 0, 200, 24)));
    }

    [Fact]
    public void QueryAtScreen_MultipleOverlaps_PrefersEntryContainingCenter()
    {
        var index = new TipIndex();
        // The true tip for this widget: a column rect fully covering it.
        index.Add(Square(0, 0), new TipRect(100, 100, 20, 20), DefaultClip, 1, 0, () => "true column tip");
        // A neighbour's tip whose rect only clips the widget's left edge —
        // overlaps the query rect (105-115) but does not reach its center.
        index.Add(Square(0, 0), new TipRect(100, 100, 8, 20), DefaultClip, 2, 0, () => "neighbour column tip");

        // Widget centered at (110, 110) — inside entry 1's rect, outside entry 2's.
        Assert.Equal("true column tip", index.QueryAtScreen(new TipRect(105, 105, 10, 10), DefaultClip));
    }

    [Fact]
    public void QueryAtScreen_MultipleOverlaps_NoneContainCenter_JoinsAllAsBefore()
    {
        // Defensive fallback: when NO overlapping entry contains the widget's
        // center (a shape this codebase has not observed), the narrowing
        // must not silently drop every candidate — it falls back to joining
        // every overlapping hit, exactly as before this fix.
        var index = new TipIndex();
        index.Add(Square(0, 0), new TipRect(0, 0, 5, 20), DefaultClip, 1, 0, () => "left sliver");
        index.Add(Square(0, 0), new TipRect(15, 0, 5, 20), DefaultClip, 2, 0, () => "right sliver");

        // Widget spans both slivers but its center (10, 10) sits in neither.
        Assert.Equal("left sliver. right sliver", index.QueryAtScreen(new TipRect(0, 0, 20, 20), DefaultClip));
    }

    [Fact]
    public void QueryAtScreen_StackedTipsOnSameRect_StillJoin()
    {
        // Genuine same-context stacking (vanilla's own multi-tip regions)
        // must survive the center-containment narrowing: both entries share
        // the exact widget rect, so both contain its center.
        var index = new TipIndex();
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "low");
        index.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 5, () => "high");
        Assert.Equal("high. low", index.QueryAtScreen(Square(2, 2), DefaultClip));
    }

    /// <summary>
    /// The captured-row folding defect: a detached capture pass
    /// spans a whole window draw, and every nested GUI group restarts its local
    /// coordinates near (0,0), so a LOCAL query over such an index overlaps
    /// every group's registration at once and <see cref="TipIndex"/> joins them
    /// all with ". " — the tooltip walls a blind player heard on every row of
    /// the "Additional controls" region. The screen+clip query returns each
    /// widget's own tooltip and nothing else, which is why CapturedRowFolder
    /// and InspectTabCaptureService resolve there and the local twin is gone.
    /// </summary>
    [Fact]
    public void QueryAt_LocalRectsAcrossGroups_FuseEveryTip_WhileQueryAtScreenDoesNot()
    {
        var index = new TipIndex();
        var seedClip = new TipClip(new TipRect(0, 0, 500, 300), 1);
        var factionClip = new TipClip(new TipRect(600, 0, 400, 300), 2);
        var buttonClip = new TipClip(new TipRect(0, 700, 1000, 60), 1);
        // Three widgets, three unrelated groups, all drawn at local (0,0).
        index.Add(Square(0, 0), Square(10, 40), seedClip, 1, 0, () => "seed tip");
        index.Add(Square(0, 0), Square(620, 40), factionClip, 2, 0, () => "faction tip");
        index.Add(Square(0, 0), Square(10, 720), buttonClip, 3, 0, () => "generate tip");

        Assert.Equal("seed tip. faction tip. generate tip", index.QueryAt(Square(1, 1)));

        Assert.Equal("seed tip", index.QueryAtScreen(Square(11, 41), seedClip));
        Assert.Equal("faction tip", index.QueryAtScreen(Square(621, 41), factionClip));
        Assert.Equal("generate tip", index.QueryAtScreen(Square(11, 721), buttonClip));
    }

    /// <summary>
    /// The harvest fallback the CLIP-AWARE resolvers spell as
    /// <c>live ?? harvest</c>: a caller-gated tip only the harvest pass could
    /// force still resolves, and a tip vanilla registered for real outranks the
    /// forced one on the same rect, because the live registration is evidence
    /// and the harvested one is inference. The local-rect resolver has no
    /// harvest fallback — without clip context a harvest hit there can only be
    /// another surface's tips matched across coordinate spaces.
    /// </summary>
    [Fact]
    public void HarvestFallback_FillsGapsButNeverOutranksTheLiveIndex()
    {
        var live = new TipIndex();
        var harvest = new TipIndex();
        live.Add(Square(0, 0), Square(0, 0), DefaultClip, 1, 0, () => "live tip");
        harvest.Add(Square(0, 0), Square(0, 0), DefaultClip, 2, 0, () => "harvested tip");
        harvest.Add(Square(50, 0), Square(50, 0), DefaultClip, 3, 0, () => "gated tip");

        Assert.Equal(
            "live tip",
            live.QueryAtScreen(Square(5, 5), DefaultClip) ?? harvest.QueryAtScreen(Square(5, 5), DefaultClip));
        Assert.Equal(
            "gated tip",
            live.QueryAtScreen(Square(55, 5), DefaultClip) ?? harvest.QueryAtScreen(Square(55, 5), DefaultClip));
    }

    /// <summary>
    /// Vanilla's float menu stacks its rows one pixel into each other
    /// (decompiled Verse/FloatMenu.cs:282), so every row's tip clips both
    /// neighbours. A row must still answer with its own tip alone.
    /// </summary>
    [Fact]
    public void QueryAt_RowsOverlappingByOnePixel_ResolveOnlyTheirOwnTip()
    {
        var index = new TipIndex();
        for (int row = 0; row < 3; row++)
        {
            TipRect rect = new(0f, row * 29f, 100f, 30f);
            int id = row;
            index.Add(rect, rect, DefaultClip, id, 0, () => "row " + id);
        }

        Assert.Equal("row 0", index.QueryAt(new TipRect(0f, 0f, 100f, 30f)));
        Assert.Equal("row 1", index.QueryAt(new TipRect(0f, 29f, 100f, 30f)));
        Assert.Equal("row 2", index.QueryAt(new TipRect(0f, 58f, 100f, 30f)));
    }

    /// <summary>
    /// The narrowing only ever removes candidates: a tip registered over a
    /// sub-rect of the element, with nothing else overlapping, still resolves.
    /// </summary>
    [Fact]
    public void QueryAt_LoneOffCentreTip_StillResolves()
    {
        var index = new TipIndex();
        TipRect corner = new(0f, 0f, 4f, 4f);
        index.Add(corner, corner, DefaultClip, 1, 0, () => "corner tip");

        Assert.Equal("corner tip", index.QueryAt(new TipRect(0f, 0f, 100f, 30f)));
    }
}
