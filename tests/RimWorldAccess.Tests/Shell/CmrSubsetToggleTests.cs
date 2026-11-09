using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="CmrSubsetToggle"/>, the tri-state arithmetic behind Colony Manager Redux's
/// shortcut rows: a partially allowed subset fills up rather than emptying, and an EMPTY subset
/// reads as unchecked, as the mod's own <c>Utilities.ToggleState</c> reports it.
/// </summary>
public class CmrSubsetToggleTests
{
    [Fact]
    public void FullyAllowedSubsetIsChecked()
    {
        Assert.Equal(CheckState.Checked, CmrSubsetToggle.StateOf(4, 4));
        Assert.False(CmrSubsetToggle.ValueForNextClick(4, 4));
    }

    [Fact]
    public void UnallowedSubsetIsUnchecked()
    {
        Assert.Equal(CheckState.Unchecked, CmrSubsetToggle.StateOf(4, 0));
        Assert.True(CmrSubsetToggle.ValueForNextClick(4, 0));
    }

    [Fact]
    public void PartiallyAllowedSubsetIsPartialAndFillsUp()
    {
        Assert.Equal(CheckState.PartiallyChecked, CmrSubsetToggle.StateOf(4, 1));
        Assert.Equal(CheckState.PartiallyChecked, CmrSubsetToggle.StateOf(4, 3));
        Assert.True(CmrSubsetToggle.ValueForNextClick(4, 3));
    }

    [Fact]
    public void EmptySubsetReadsAsUnchecked()
    {
        Assert.Equal(CheckState.Unchecked, CmrSubsetToggle.StateOf(0, 0));
        Assert.True(CmrSubsetToggle.ValueForNextClick(0, 0));
    }
}
