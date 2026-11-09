using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class SliderCaptionTests
{
    [Fact]
    public void EffectiveLabel_PrefersVanillasOwnLabel()
    {
        Assert.Equal("Volume", SliderCaption.EffectiveLabel("Volume", "Name", "50%", 0f, 1f));
    }

    [Fact]
    public void EffectiveLabel_FallsBackToLeftAlignedName()
    {
        Assert.Equal("Name", SliderCaption.EffectiveLabel(null, "Name", "50%", 0f, 1f));
    }

    [Fact]
    public void EffectiveLabel_TreatsSpacerLabelAsAbsent()
    {
        Assert.Equal("Name", SliderCaption.EffectiveLabel(" ", "Name", "50%", 0f, 1f));
    }

    [Fact]
    public void EffectiveLabel_RejectsVanillasBoundMarkers()
    {
        // Widgets.HorizontalSlider(Rect, ref float, FloatRange, ...) passes the two bounds here.
        Assert.Equal("", SliderCaption.EffectiveLabel(null, "0", "100", 0f, 100f));
    }

    [Fact]
    public void EffectiveValueText_RejectsVanillasBoundMarkers()
    {
        Assert.Equal("", SliderCaption.EffectiveValueText("0", "100", 0f, 100f));
    }

    [Fact]
    public void EffectiveLabel_RejectsGroupedAndUnitedBoundMarkers()
    {
        // Progression: Education captions its semester-goal slider this way.
        string left = 1000f.ToString("N0") + " xp";
        string right = 100000f.ToString("N0") + " xp";
        Assert.Equal("", SliderCaption.EffectiveLabel(null, left, right, 1000f, 100000f));
    }

    [Fact]
    public void EffectiveValueText_RejectsGroupedAndUnitedBoundMarkers()
    {
        string left = 1000f.ToString("N0") + " xp";
        string right = 100000f.ToString("N0") + " xp";
        Assert.Equal("", SliderCaption.EffectiveValueText(left, right, 1000f, 100000f));
    }

    [Fact]
    public void EffectiveLabel_KeepsANameThatMerelyMentionsANumber()
    {
        // The bound has to OPEN the caption, so the "1" here stays part of a name.
        string right = 1000f.ToString("N0") + " xp";
        Assert.Equal("Tier 1 output",
            SliderCaption.EffectiveLabel(null, "Tier 1 output", right, 1f, 1000f));
    }

    [Fact]
    public void EffectiveValueText_KeepsACallersOwnRendering()
    {
        Assert.Equal("+50%", SliderCaption.EffectiveValueText("Name", "+50%", 0f, 1f));
    }

    [Fact]
    public void ResolveName_PrefersTheWidgetsOwnCaption()
    {
        SliderName name = SliderCaption.ResolveName("Volume", null, null, 0f, 1f, "Wrapper", "Remembered", true);
        Assert.Equal("Volume", name.Text);
        Assert.True(name.Live);
        Assert.False(name.CarriesValue);
    }

    [Fact]
    public void ResolveName_FallsBackToTheWrappersDiscardedCaption()
    {
        SliderName name = SliderCaption.ResolveName(null, null, null, 0f, 1f, "Pan speed", "Remembered", true);
        Assert.Equal("Pan speed", name.Text);
        Assert.True(name.Live);
    }

    [Fact]
    public void ResolveName_FallsBackToTheRememberedRowLast()
    {
        SliderName name = SliderCaption.ResolveName(null, null, null, 0f, 1f, null, "Threshold", true);
        Assert.Equal("Threshold", name.Text);
        Assert.False(name.Live);
        Assert.True(name.CarriesValue);
    }

    [Fact]
    public void ResolveName_IsEmptyWhenNothingNamedTheSlider()
    {
        SliderName name = SliderCaption.ResolveName(null, "0", "100", 0f, 100f, null, null, false);
        Assert.Equal("", name.Text);
        Assert.False(name.Live);
    }

    [Theory]
    [InlineData(3f, "3")]
    [InlineData(0.25f, "0.25")]
    public void FormatValue_UsesThePrecisionTheValueCarries(float value, string expected)
    {
        Assert.Equal(expected, SliderCaption.FormatValue(value));
    }
}

public class SliderCaptionFractionalTests
{
    [Theory]
    [InlineData("Sex volume: 200%", 2f, 0f, 2f, true)]
    [InlineData("Cum filth amount: 0%", 0f, 0f, 5f, true)]
    [InlineData("Decay rate (%): 100% [Not recommended]", 1f, 0f, 5f, true)]
    [InlineData("Taux: 12,5%", 0.125f, 0f, 1f, true)]
    [InlineData("Chance: 50%", 50f, 0f, 100f, false)]
    [InlineData("Cooldown: 3h", 3f, 0f, 100f, false)]
    [InlineData("Threshold: 3", 3f, 0f, 5f, false)]
    [InlineData("", 1f, 0f, 5f, false)]
    public void CaptionImpliesFractional_OnlyForPercentScaledSmallRanges(string caption, float value, float min, float max, bool expected)
    {
        Assert.Equal(expected, SliderCaption.CaptionImpliesFractional(caption, value, min, max));
    }
}

public class SliderCaptionTrailingValueTests
{
    [Theory]
    [InlineData("Speed Multiplier 2", 2f, "Speed Multiplier")]
    [InlineData("Meditation Focus Strength 0.75", 0.75f, "Meditation Focus Strength")]
    [InlineData("Facteur 1,5", 1.5f, "Facteur")]
    [InlineData("Speed Multiplier 2", 3f, null)]
    [InlineData("Cooldown 3h", 3f, null)]
    [InlineData("Version 1,000", 1f, null)]
    [InlineData("2", 2f, null)]
    [InlineData("", 2f, null)]
    public void NameBeforeTrailingValue_StripsOnlyTheSlidersOwnValue(string caption, float value, string expected)
    {
        Assert.Equal(expected, SliderCaption.NameBeforeTrailingValue(caption, value));
    }
}
