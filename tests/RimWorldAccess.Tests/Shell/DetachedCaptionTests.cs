using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class DetachedCaptionTests
{
    private static LineBox Box(float x, float y, float width, float height)
    {
        return new LineBox { X = x, Y = y, Width = width, Height = height };
    }

    // A bare Widgets.RadioButton at x - 32 with its caption Label drawn first at x.
    private static readonly LineBox Radio = Box(1502f, 198f, 24f, 24f);

    [Fact]
    public void TrailsControl_AcceptsACaptionJustRightOnTheSameLine()
    {
        Assert.True(DetachedCaption.TrailsControl(Radio, Box(1534f, 198f, 373f, 22f)));
    }

    [Fact]
    public void TrailsControl_RejectsACaptionFarToTheRight()
    {
        Assert.False(DetachedCaption.TrailsControl(Radio, Box(1600f, 198f, 200f, 22f)));
    }

    [Fact]
    public void TrailsControl_RejectsACaptionToTheLeft()
    {
        Assert.False(DetachedCaption.TrailsControl(Radio, Box(1079f, 198f, 400f, 22f)));
    }

    [Fact]
    public void TrailsControl_RejectsACaptionOnTheNextLine()
    {
        Assert.False(DetachedCaption.TrailsControl(Radio, Box(1534f, 224f, 373f, 22f)));
    }

    // Caption at 132, a reset button between at 167..196, the slider at 204.
    private static readonly LineBox Slider = Box(208f, 204f, 864f, 22f);

    [Fact]
    public void AboveInReach_AcceptsACaptionTwoRowsUpInTheSameColumn()
    {
        Assert.True(DetachedCaption.AboveInReach(Slider, Box(208f, 132f, 864f, 22f)));
    }

    [Fact]
    public void AboveInReach_RejectsACaptionBeyondThreeRowHeights()
    {
        Assert.False(DetachedCaption.AboveInReach(Slider, Box(208f, 100f, 864f, 22f)));
    }

    [Fact]
    public void AboveInReach_RejectsAnotherColumn()
    {
        Assert.False(DetachedCaption.AboveInReach(Slider, Box(1100f, 132f, 400f, 22f)));
    }

    [Fact]
    public void AboveInReach_RejectsACaptionBelow()
    {
        Assert.False(DetachedCaption.AboveInReach(Slider, Box(208f, 230f, 864f, 22f)));
    }

    [Fact]
    public void ValueCaption_IsRecognisedByItsTrailingValue()
    {
        Assert.NotNull(SliderCaption.NameBeforeTrailingValue("Max number of memes allowed (default is 4): 4", 4f));
        Assert.Null(SliderCaption.NameBeforeTrailingValue("Max number of memes allowed (default is 4): 4", 5f));
    }

    [Fact]
    public void TrailingPercentReadout_RendersTheSliderValue()
    {
        Assert.True(ValueLabeledSlider.LabelIsValue("100%", 1f));
        Assert.True(ValueLabeledSlider.CaptionsOnRow(Box(812f, 276f, 173f, 22f), Box(985f, 276f, 86f, 22f)));
    }

    [Fact]
    public void MarksFieldEnd_SearchIconLeftOrRightOfTheField()
    {
        LineBox quickSearchField = Box(434f, 140f, 280f, 24f);
        Assert.True(DetachedCaption.MarksFieldEnd(quickSearchField, Box(412f, 143f, 18f, 18f)));
        LineBox xmlField = Box(200f, 100f, 300f, 22f);
        Assert.True(DetachedCaption.MarksFieldEnd(xmlField, Box(500f, 100f, 22f, 22f)));
    }

    [Fact]
    public void MarksFieldEnd_RejectsIconsOffTheFieldLineOrFarAway()
    {
        LineBox field = Box(200f, 100f, 300f, 22f);
        Assert.False(DetachedCaption.MarksFieldEnd(field, Box(500f, 140f, 22f, 22f)));
        Assert.False(DetachedCaption.MarksFieldEnd(field, Box(560f, 100f, 22f, 22f)));
        Assert.False(DetachedCaption.MarksFieldEnd(field, Box(150f, 100f, 22f, 22f)));
        Assert.False(DetachedCaption.MarksFieldEnd(field, Box(200f, 100f, 400f, 22f)));
    }
}
