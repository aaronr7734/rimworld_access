using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="TabStripDetector"/>, the geometry-only recognizer
/// that gives a hand-rolled row (or column) of buttons the same tab-bar
/// grammar the engine's own tab widget gets. Every shape here is pinned from
/// the 2026-08-02 census of real mod geometry, not
/// invented — a regression here silently turns a real strip back into a wall
/// of buttons the player must arrow through one at a time.
/// </summary>
public class TabStripDetectorTests
{
    private static TabStripRow Button(float x, float width, float y = 0f, float height = 30f, int clip = 1)
    {
        return new TabStripRow { Kind = TabStripRowKind.Button, ClipId = clip, X = x, Y = y, Width = width, Height = height };
    }

    private static TabStripRow Of(TabStripRowKind kind)
    {
        return new TabStripRow { Kind = kind, ClipId = 1, X = 0f, Y = 200f, Width = 100f, Height = 24f };
    }

    /// <summary>RimTalk's settings page: four equal buttons tiled edge to edge across the top, 0px seams.</summary>
    private static List<TabStripRow> FourTiledTabs()
    {
        return new List<TabStripRow>
        {
            Button(0f, 220f),
            Button(220f, 220f),
            Button(440f, 220f),
            Button(660f, 220f),
            Of(TabStripRowKind.Content),
        };
    }

    [Fact]
    public void FourTiledLeadingButtons_AreATier1Strip()
    {
        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(FourTiledTabs(), out result));
        Assert.Equal(new List<int> { 0, 1, 2, 3 }, result.Members);
        Assert.Equal(TabStripOrientation.Horizontal, result.Orientation);
        Assert.True(result.ShapeAloneSuffices);
    }

    [Fact]
    public void ChromeAndCaptionsMayPrecedeTheStrip()
    {
        List<TabStripRow> rows = new List<TabStripRow> { Of(TabStripRowKind.Chrome), Of(TabStripRowKind.Passive) };
        rows.AddRange(FourTiledTabs());

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(new List<int> { 2, 3, 4, 5 }, result.Members);
    }

    [Fact]
    public void AButtonRowBelowRealContent_IsAToolbarNotAStrip()
    {
        // The Content row leads the surface, so the search refuses before it
        // ever looks at the (otherwise perfectly tiled) buttons after it.
        List<TabStripRow> rows = new List<TabStripRow> { Of(TabStripRowKind.Content) };
        rows.AddRange(FourTiledTabs());

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Null(result.Members);
    }

    [Fact]
    public void AnAcceptCancelPair_IsNeverAStrip()
    {
        // Equal-size, same-band, edge-adjacent — everything a strip needs
        // except a third member, which is exactly the point of MinimumTabs.
        List<TabStripRow> rows = new List<TabStripRow> { Button(0f, 200f), Button(200f, 200f) };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void ANonFittingButtonAfterTheStrip_EndsItWithoutFailingIt()
    {
        // RimTalk's settings draw their four tabs and then a full-width
        // "Switch to Simple Settings" button in the SAME band (live
        // regression, 2026-08-02): an odd neighbour marks where the page's
        // own content begins — like a Content row — never proof that the
        // strip before it was fake.
        List<TabStripRow> rows = FourTiledTabs();
        rows.Insert(4, Button(0f, 880f)); // full-width, same band as the tabs

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(new List<int> { 0, 1, 2, 3 }, result.Members);
        Assert.True(result.ShapeAloneSuffices);
    }

    [Fact]
    public void LiteratureSettingsShape_SixPixelSeams_IsATier1Strip()
    {
        // The RimTalk Literature settings page (four Widgets.ButtonText rows,
        // NOT TabDrawer): the census item MaximumSeam was raised to close.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 200f),
            Button(206f, 200f), // 6px seam
            Button(412f, 200f), // 6px seam
            Button(618f, 200f), // 6px seam
            Of(TabStripRowKind.Content), // the settings page below the tabs
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.True(result.ShapeAloneSuffices);
    }

    [Fact]
    public void VanillaHistoryLegendShape_FourPixelSeams_IsATier1Strip()
    {
        // MainTabWindow_History's own hand-rolled day-range legend row: four
        // 110px-wide buttons, 4px seams — vanilla's own evidence that a real
        // strip is not always seamless.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 110f),
            Button(114f, 110f),
            Button(228f, 110f),
            Button(342f, 110f),
            // The window's own Select graph button on the next band down —
            // out of the strip's band, so it ends the run, and the page
            // beyond that the strip needs.
            Button(0f, 110f, y: 40f),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(4, result.Members.Count);
        Assert.True(result.ShapeAloneSuffices);
    }

    [Fact]
    public void ColonyManagerReduxBar_ThreeClusters_IsATier2StripSortedByX()
    {
        // Colony Manager Redux's real toolbar: 2 + 7 + 1 buttons in three
        // visual clusters (32px squares, Constants.Margin = 6px intra-cluster
        // seams, >=64px between clusters). Inserted in DRAW order (Left,
        // Right, Middle) to prove the result sorts by visual X regardless of
        // insertion order — the mod draws its Import/Export cluster (here
        // "Right") before its single Overview button ("Middle"), which sits
        // between Left and Right on screen.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            // Left cluster: 2 buttons, x 0..70.
            Button(0f, 32f),
            Button(38f, 32f), // 6px seam
            // Right cluster: 7 buttons, x 300..560 (drawn before Middle).
            Button(300f, 32f),
            Button(338f, 32f), // 6px seam
            Button(376f, 32f),
            Button(414f, 32f),
            Button(452f, 32f),
            Button(490f, 32f),
            Button(528f, 32f),
            // Middle cluster: 1 button, x 150..182 — visually between Left and Right.
            Button(150f, 32f),
            Of(TabStripRowKind.Content), // the manager page below the bar
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(TabStripOrientation.Horizontal, result.Orientation);
        Assert.False(result.ShapeAloneSuffices);
        // Visual order: Left(0,1), Middle(index 9), Right(2..8).
        Assert.Equal(new List<int> { 0, 1, 9, 2, 3, 4, 5, 6, 7, 8 }, result.Members);
    }

    [Fact]
    public void VerticalColumn_IsATier2StripNeverTier1()
    {
        // ExpandMemory's memory-type filter: the only vertical strip the
        // census found. Same X/width, small vertical seams — recognised, but
        // a stacked column of same-size buttons is also what a settings
        // menu's own button list looks like, so it can never promote on
        // shape alone.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 200f, y: 0f),
            Button(0f, 200f, y: 32f),  // 2px seam (30 height + 2)
            Button(0f, 200f, y: 64f),  // 2px seam
            // The filtered memory list beside the column — the page the
            // filter switches.
            new TabStripRow { Kind = TabStripRowKind.Content, ClipId = 1, X = 220f, Y = 0f, Width = 400f, Height = 24f },
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(TabStripOrientation.Vertical, result.Orientation);
        Assert.False(result.ShapeAloneSuffices);
    }

    [Fact]
    public void ButtonsOfDifferentWidths_AreNotTiled()
    {
        List<TabStripRow> rows = new List<TabStripRow> { Button(0f, 100f), Button(100f, 160f), Button(260f, 100f) };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void ButtonsInDifferentClipContexts_AreNeverTrustedOnShapeAlone()
    {
        // Colony Manager Redux draws its ONE tab bar as three GUI.BeginGroup
        // clusters, so a clip boundary cannot refuse membership outright —
        // but a strip stitched across clips is also what two unrelated
        // side-by-side panels could produce, so it always demands the
        // selection verdict (tier 2), even when its seams are hairline.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 200f, clip: 1),
            Button(200f, 200f, clip: 2),
            Button(400f, 200f, clip: 2),
            Of(TabStripRowKind.Content),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.False(result.ShapeAloneSuffices);
    }

    [Fact]
    public void ColonyManagerReduxBar_ThreeClipsOneDisabledTab_IsOneTier2Strip()
    {
        // The full live shape (2026-08-02 smoke): left cluster (2 icons,
        // clip 1) drawn first, then the right cluster (1 icon, clip 2), then
        // the middle cluster (6 icons, clip 3) with the disabled Power tab
        // rendered as a fitting Passive line among them. One strip, members
        // in visual X order, tier 2.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(18f, 64f, 18f, 64f, clip: 1),
            Button(118f, 64f, 18f, 64f, clip: 1),
            Button(960f, 64f, 18f, 64f, clip: 2),
            Button(382f, 64f, 18f, 64f, clip: 3),
            Button(482f, 64f, 18f, 64f, clip: 3),
            Button(582f, 64f, 18f, 64f, clip: 3),
            Button(682f, 64f, 18f, 64f, clip: 3),
            Button(782f, 64f, 18f, 64f, clip: 3),
            Button(882f, 64f, 18f, 64f, clip: 3),
            new TabStripRow { Kind = TabStripRowKind.Passive, ClipId = 3, X = 982f, Y = 18f, Width = 64f, Height = 64f },
            Of(TabStripRowKind.Content),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(new List<int> { 0, 1, 3, 4, 5, 6, 7, 8, 2 }, result.Members);
        Assert.Equal(TabStripOrientation.Horizontal, result.Orientation);
        Assert.False(result.ShapeAloneSuffices);
    }

    [Fact]
    public void SeamsWiderThanTheCap_MakeTheStripTier2_NotAFailure()
    {
        // Colony Manager Redux's icon gaps land wherever its window width
        // puts them (7 logical pixels, live) — real strips produce arbitrary
        // gap widths, so a wide seam demotes the strip to tier 2 (promoted
        // only with a selection verdict) rather than refusing it. An earlier
        // draft's "dead zone" rule between tight tiling and a cluster gap
        // rejected that live strip.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 100f),
            Button(109f, 100f), // 9px seam
            Button(218f, 100f), // 9px seam
            Of(TabStripRowKind.Content),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.False(result.ShapeAloneSuffices);
    }

    [Fact]
    public void AFittingPassiveRow_IsSkippedNotAStripBreak()
    {
        // A disabled tab reads as a Passive row (Colony Manager Redux's
        // unresearched Power tab, rendered as a read-only line): when its
        // rect fits the strip's own shape it is window content INSIDE the
        // bar, so the enabled tabs promote around it and the line itself
        // stays a navigable row. A passive row of any other shape still ends
        // the run.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 64f, 0f, 64f),
            Button(100f, 64f, 0f, 64f),
            new TabStripRow { Kind = TabStripRowKind.Passive, ClipId = 1, X = 200f, Y = 0f, Width = 64f, Height = 64f },
            Button(300f, 64f, 0f, 64f),
            Button(400f, 64f, 0f, 64f),
            Of(TabStripRowKind.Content),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(new List<int> { 0, 1, 3, 4 }, result.Members);
        Assert.False(result.ShapeAloneSuffices);
    }

    [Fact]
    public void OverlappingRects_AreNeverAStrip()
    {
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 100f),
            Button(50f, 100f), // -50px seam: well past even the raised tolerance
            Button(200f, 100f),
        };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void SubPixelResidueFromDividingAWidth_StillTiles()
    {
        // width / 3 over 700 leaves a fraction on every edge.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 233.33f),
            Button(233.33f, 233.34f),
            Button(466.67f, 233.33f),
            Of(TabStripRowKind.Content),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(3, result.Members.Count);
    }

    [Fact]
    public void TheRunStopsAtTheFirstNonButton()
    {
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 200f),
            Button(200f, 200f),
            Button(400f, 200f),
            Of(TabStripRowKind.Passive),
            Button(600f, 200f),
        };

        TabStripResult result;
        Assert.True(TabStripDetector.TryFindLeadingStrip(rows, out result));
        Assert.Equal(3, result.Members.Count);
    }

    [Fact]
    public void DialogBottomActionRow_NothingBeneath_IsNotAStrip()
    {
        // Rimworld Together's Marketplace (live, 2026-08-03): title and
        // description labels up top, an empty scroll list, then
        // Personal/Refresh/Close tiled edge to edge across the window's
        // bottom by dividing its width by three — the exact tier-1 band.
        // Promoting it made Tab press Refresh and the next Tab press Close.
        // Nothing is ever drawn beneath a bottom action row, and that is
        // what refuses it.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            new TabStripRow { Kind = TabStripRowKind.Passive, ClipId = 1, X = 150f, Y = 0f, Width = 150f, Height = 30f },
            new TabStripRow { Kind = TabStripRowKind.Passive, ClipId = 1, X = 130f, Y = 40f, Width = 190f, Height = 22f },
            Button(0f, 150f, y: 380f),
            Button(150f, 150f, y: 380f),
            Button(300f, 150f, y: 380f),
        };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void VerticalColumnAlone_NothingBeside_IsNotAStrip()
    {
        // A stacked column with no page next to it is a menu of actions,
        // however evenly it tiles.
        List<TabStripRow> rows = new List<TabStripRow>
        {
            Button(0f, 200f, y: 0f),
            Button(0f, 200f, y: 32f),
            Button(0f, 200f, y: 64f),
        };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void NoButtonsAtAll_IsNotAStrip()
    {
        List<TabStripRow> rows = new List<TabStripRow> { Of(TabStripRowKind.Passive), Of(TabStripRowKind.Chrome) };

        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(rows, out result));
    }

    [Fact]
    public void EmptyInput_IsNotAStrip()
    {
        TabStripResult result;
        Assert.False(TabStripDetector.TryFindLeadingStrip(new List<TabStripRow>(), out result));
        Assert.False(TabStripDetector.TryFindLeadingStrip(null, out result));
    }

    private static TabStripDetector.TabTint Tint(float r, float g, float b, float a = 1f)
    {
        return new TabStripDetector.TabTint { R = r, G = g, B = b, A = a };
    }

    [Fact]
    public void ActiveWhiteAmongGray_ReturnsThatIndex()
    {
        // RimTalk's own direction: active tab drawn at full strength, the
        // rest dimmed.
        List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint>
        {
            Tint(0.6f, 0.6f, 0.6f),
            Tint(0.6f, 0.6f, 0.6f),
            Tint(1f, 1f, 1f),
            Tint(0.6f, 0.6f, 0.6f),
        };
        Assert.Equal(2, TabStripDetector.IndexOfDistinctTint(tints));
    }

    [Fact]
    public void ActiveTintedAmongWhite_ReturnsThatIndex()
    {
        // Colony Manager Redux's own direction: the active tab is DARKER
        // (its own mouseover tint) among otherwise plain white tabs — the
        // case a brightest-wins rule reads backwards.
        List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint>
        {
            Tint(1f, 1f, 1f),
            Tint(0.7f, 0.7f, 0.9f),
            Tint(1f, 1f, 1f),
            Tint(1f, 1f, 1f),
        };
        Assert.Equal(1, TabStripDetector.IndexOfDistinctTint(tints));
    }

    [Fact]
    public void AllUniformTints_ReportNoSelection()
    {
        List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(1f, 1f, 1f), Tint(1f, 1f, 1f) };
        Assert.Equal(-1, TabStripDetector.IndexOfDistinctTint(tints));
    }

    [Fact]
    public void TwoDeviantTints_ReportNoSelection()
    {
        // Two members disagree with the rest — there is no single "odd one
        // out", so inventing a winner would be a guess, not a verdict.
        List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint>
        {
            Tint(1f, 1f, 1f),
            Tint(0.5f, 0.5f, 0.5f),
            Tint(1f, 1f, 1f),
            Tint(0.4f, 0.4f, 0.6f),
        };
        Assert.Equal(-1, TabStripDetector.IndexOfDistinctTint(tints));
    }

    [Fact]
    public void TwoTints_ReportNoSelection()
    {
        // With only two members, "the odd one out" has no meaning.
        List<TabStripDetector.TabTint> tints = new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(0.5f, 0.5f, 0.5f) };
        Assert.Equal(-1, TabStripDetector.IndexOfDistinctTint(tints));
    }

    [Fact]
    public void IndexOfMarkedOption_TheOneButtonDrawnWithoutBackground()
    {
        var tints = new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(1f, 1f, 1f), Tint(1f, 1f, 1f), Tint(1f, 1f, 1f) };
        Assert.Equal(2, TabStripDetector.IndexOfMarkedOption(tints, new List<bool> { false, false, true, false }));
        Assert.Equal(-1, TabStripDetector.IndexOfMarkedOption(tints, new List<bool> { true, false, true, false }));
        Assert.Equal(-1, TabStripDetector.IndexOfMarkedOption(tints, new List<bool> { false, false, false, false }));
    }

    [Fact]
    public void IndexOfMarkedOption_PairReadsTheTintedMemberAgainstWhite()
    {
        var bare = new List<bool> { false, false };
        Assert.Equal(1, TabStripDetector.IndexOfMarkedOption(new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(0f, 1f, 0f) }, bare));
        Assert.Equal(0, TabStripDetector.IndexOfMarkedOption(new List<TabStripDetector.TabTint> { Tint(0f, 1f, 0f), Tint(1f, 1f, 1f) }, bare));
        Assert.Equal(-1, TabStripDetector.IndexOfMarkedOption(new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(1f, 1f, 1f) }, bare));
        Assert.Equal(-1, TabStripDetector.IndexOfMarkedOption(new List<TabStripDetector.TabTint> { Tint(0.5f, 0.5f, 0.5f), Tint(0f, 1f, 0f) }, bare));
    }

    [Fact]
    public void IndexOfMarkedOption_RunOfThreeFallsBackToTheDistinctTint()
    {
        var tints = new List<TabStripDetector.TabTint> { Tint(1f, 1f, 1f), Tint(0f, 1f, 0f), Tint(1f, 1f, 1f) };
        Assert.Equal(1, TabStripDetector.IndexOfMarkedOption(tints, new List<bool> { false, false, false }));
    }
}
