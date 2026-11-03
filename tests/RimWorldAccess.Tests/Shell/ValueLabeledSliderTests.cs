using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class ValueLabeledSliderTests
{
    // Carry Different Medicine Types' Dialog_SetMedicines.DoRow: Label at x 0..200, DefIcon at
    // 250..280, slider at 330..630, all 30 high on one line.
    private static readonly LineBox Caption = Box(0f, 51f, 200f, 30f);
    private static readonly LineBox Icon = Box(250f, 51f, 30f, 30f);
    private static readonly LineBox Slider = Box(330f, 51f, 300f, 30f);

    private static LineBox Box(float x, float y, float width, float height)
    {
        return new LineBox { X = x, Y = y, Width = width, Height = height };
    }

    [Theory]
    [InlineData("0", 0f)]
    [InlineData("3", 3f)]
    [InlineData("3", 3.2f)]
    [InlineData("40%", 0.4f)]
    [InlineData("40 %", 40f)]
    [InlineData("1,000", 1000f)]
    [InlineData("0,5", 0.5f)]
    [InlineData("12 kg", 12f)]
    [InlineData("-5", -5f)]
    public void LabelIsValue_AcceptsTheRenderedValue(string label, float value)
    {
        Assert.True(ValueLabeledSlider.LabelIsValue(label, value));
    }

    [Theory]
    [InlineData("Herbal medicine", 0f)]
    [InlineData("4", 3f)]
    [InlineData("40%", 0.5f)]
    [InlineData("Tier 1", 1f)]
    [InlineData("1 of 3", 1f)]
    [InlineData("3 colonists", 3f)]
    [InlineData("", 0f)]
    [InlineData("-", 0f)]
    public void LabelIsValue_RejectsNamesAndOtherNumbers(string label, float value)
    {
        Assert.False(ValueLabeledSlider.LabelIsValue(label, value));
    }

    [Fact]
    public void CaptionsOnRow_AcceptsALeftLabelOnTheSameLine()
    {
        Assert.True(ValueLabeledSlider.CaptionsOnRow(Caption, Slider));
    }

    [Fact]
    public void CaptionsOnRow_RejectsALabelRightOfTheSlider()
    {
        Assert.False(ValueLabeledSlider.CaptionsOnRow(Box(640f, 51f, 200f, 30f), Slider));
    }

    [Fact]
    public void CaptionsOnRow_RejectsALabelOverlappingTheSlider()
    {
        Assert.False(ValueLabeledSlider.CaptionsOnRow(Box(0f, 51f, 400f, 30f), Slider));
    }

    [Fact]
    public void CaptionsOnRow_RejectsTheRowAbove()
    {
        Assert.False(ValueLabeledSlider.CaptionsOnRow(Box(0f, 11f, 200f, 30f), Slider));
    }

    [Fact]
    public void IconBetween_AcceptsTheIconBetweenCaptionAndSlider()
    {
        Assert.True(ValueLabeledSlider.IconBetween(Caption, Icon, Slider));
    }

    [Fact]
    public void IconBetween_RejectsAnIconPastTheSlider()
    {
        Assert.False(ValueLabeledSlider.IconBetween(Caption, Box(640f, 51f, 30f, 30f), Slider));
    }

    [Fact]
    public void IconBetween_RejectsAnIconOnAnotherLine()
    {
        Assert.False(ValueLabeledSlider.IconBetween(Caption, Box(250f, 91f, 30f, 30f), Slider));
    }
}
