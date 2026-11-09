using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>Tests for <see cref="RedundantLabelPrefix.Strip"/>.</summary>
public class RedundantLabelPrefixTests
{
    [Theory]
    [InlineData("Use global hotkeys. When enabled, tools...", "Use global hotkeys", "When enabled, tools...")]
    [InlineData("Use global hotkeys", "Use global hotkeys", "")]
    [InlineData("Volume: how loud", "Volume", "how loud")]
    [InlineData("Hotkeys are disabled while paused", "Hotkeys", "Hotkeys are disabled while paused")]
    [InlineData("Use global hotkeys, when enabled", "Use global hotkeys", "Use global hotkeys, when enabled")]
    [InlineData("use global hotkeys. Something", "Use global hotkeys", "use global hotkeys. Something")]
    [InlineData("  Volume. Loud", "Volume", "Loud")]
    [InlineData("Volume.\nLoud", "Volume", "Loud")]
    [InlineData("Strong\n\nHauls more.", "Strong (good)", "Hauls more.")]
    [InlineData("Strong", "Strong (good)", "")]
    [InlineData("Strong\nHauls more.", "Strongest (good)", "Strong\nHauls more.")]
    [InlineData("Strong (good) and fast\nHauls more.", "Strong (good)", "Strong (good) and fast\nHauls more.")]
    public void Strip_MatchesExpected(string extras, string label, string expected)
    {
        Assert.Equal(expected, RedundantLabelPrefix.Strip(extras, label));
    }

    [Theory]
    [InlineData("Firefoam pop\nDescription", "Firefoam pop", "Description")]
    [InlineData("Ann (age 30)\nColonist", "Ann", "(age 30)\nColonist")]
    [InlineData("Ann: leader\nColonist", "Ann", "leader\nColonist")]
    [InlineData("Other\nColonist", "Ann", "Other\nColonist")]
    [InlineData("Ann", "Ann", "")]
    public void AfterTitle_MatchesExpected(string tooltip, string title, string expected)
    {
        Assert.Equal(expected, RedundantLabelPrefix.AfterTitle(tooltip, title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Strip_NullOrEmptyLabel_ReturnsExtrasUnchanged(string label)
    {
        Assert.Equal("anything", RedundantLabelPrefix.Strip("anything", label!));
    }

    [Fact]
    public void Strip_NullExtras_ReturnsNull()
    {
        Assert.Null(RedundantLabelPrefix.Strip(null!, "anything"));
    }
}
