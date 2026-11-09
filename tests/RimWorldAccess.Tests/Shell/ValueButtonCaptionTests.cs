using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class ValueButtonCaptionTests
{
    private const int ContentClip = 1;
    private const int FrameClip = 2;

    private static ValueButtonPart Part(float x, float y, float width, float height, int clip = ContentClip, bool chrome = false)
    {
        return new ValueButtonPart
        {
            Box = new LineBox { X = x, Y = y, Width = width, Height = height },
            ClipId = clip,
            IsChrome = chrome,
        };
    }

    [Fact]
    public void RowWideLabel_CaptionsTheButtonOnItsLine()
    {
        // Listing_Standard.ButtonTextLabeled: a full-row label with the button in its right part.
        ValueButtonPart label = Part(0f, 100f, 600f, 30f);
        ValueButtonPart button = Part(400f, 100f, 200f, 30f);
        Assert.True(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: false, buttonLeadsCluster: false));
    }

    [Fact]
    public void ContentLine_NeverCaptionsTheWindowCloseButton()
    {
        // Tech Advancing's last legend line ends level with the frame's bottom Close button.
        ValueButtonPart legend = Part(183f, 769f, 914f, 22f);
        ValueButtonPart close = Part(580f, 755f, 120f, 40f, FrameClip, chrome: true);
        Assert.False(ValueButtonCaption.Captions(legend, close, labelDrawnJustBefore: false, buttonLeadsCluster: false));
    }

    [Fact]
    public void ContentLine_OverTheCloseButton_IsAnOnLineHostByGeometryAlone()
    {
        // Why the layer gate exists: the same pair in one layer passes the row-wide test.
        Assert.True(ValueButtonCaption.RowHostsButton(
            new LineBox { X = 183f, Y = 769f, Width = 914f, Height = 22f },
            new LineBox { X = 580f, Y = 755f, Width = 120f, Height = 40f }));
    }

    [Fact]
    public void LineAboveTheButton_DoesNotCaptionIt()
    {
        ValueButtonPart ultra = Part(183f, 747f, 914f, 22f);
        ValueButtonPart close = Part(580f, 755f, 120f, 40f);
        Assert.False(ValueButtonCaption.Captions(ultra, close, labelDrawnJustBefore: false, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideButton_CaptionsIt()
    {
        // Better Sliders' settings: label then button on one line, side by side.
        ValueButtonPart label = Part(208f, 108f, 689f, 22f);
        ValueButtonPart button = Part(899f, 108f, 173f, 22f);
        Assert.True(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: true, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideButton_NeedsNothingDrawnBetween()
    {
        ValueButtonPart label = Part(208f, 108f, 689f, 22f);
        ValueButtonPart button = Part(899f, 108f, 173f, 22f);
        Assert.False(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: false, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideButton_OnTheLineAbove_DoesNotCaptionIt()
    {
        ValueButtonPart label = Part(208f, 108f, 689f, 22f);
        ValueButtonPart button = Part(899f, 130f, 173f, 22f);
        Assert.False(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: true, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideButton_InAnotherClip_DoesNotCaptionIt()
    {
        ValueButtonPart label = Part(208f, 108f, 689f, 22f);
        ValueButtonPart button = Part(899f, 108f, 173f, 22f, FrameClip);
        Assert.False(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: true, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideButton_OverlappingTheButton_IsNotBeside()
    {
        // Narrower than the button, so not row-wide either.
        ValueButtonPart label = Part(208f, 108f, 120f, 22f);
        ValueButtonPart button = Part(300f, 108f, 173f, 22f);
        Assert.False(ValueButtonCaption.Captions(label, button, labelDrawnJustBefore: true, buttonLeadsCluster: false));
    }

    [Fact]
    public void LabelBesideAButtonRun_NamesTheRunNotItsFirstMember()
    {
        ValueButtonPart label = Part(0f, 10f, 80f, 24f);
        ValueButtonPart first = Part(82f, 10f, 100f, 24f);
        Assert.False(ValueButtonCaption.Captions(label, first, labelDrawnJustBefore: true, buttonLeadsCluster: true));
    }

    [Fact]
    public void LabelFarLeftOfButton_DoesNotCaptionIt()
    {
        ValueButtonPart version = Part(0f, 500f, 60f, 22f);
        ValueButtonPart ok = Part(400f, 500f, 120f, 22f);
        Assert.False(ValueButtonCaption.Captions(version, ok, labelDrawnJustBefore: true, buttonLeadsCluster: false));
    }
}
