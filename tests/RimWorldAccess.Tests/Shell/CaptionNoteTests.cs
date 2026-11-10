using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class CaptionNoteTests
{
    private static LineBox Box(float x, float y, float width, float height)
    {
        return new LineBox { X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void IsWholeColorSpan_AcceptsADimmedNote()
    {
        Assert.True(CaptionNote.IsWholeColorSpan("<color=#888888>Maximum value any stat can reach.</color>"));
        Assert.True(CaptionNote.IsWholeColorSpan("  <color=#C8A02E>Requires restarting the game.</color> "));
    }

    [Fact]
    public void IsWholeColorSpan_RejectsACaptionWithAColouredPart()
    {
        Assert.False(CaptionNote.IsWholeColorSpan("Max Stat Value: Unlimited  <color=#888888>(Default: 9999)</color>"));
        Assert.False(CaptionNote.IsWholeColorSpan("Production Time: <color=#87F6F6FF>1 year</color>"));
        Assert.False(CaptionNote.IsWholeColorSpan("<color=#FF4444>S</color> and <color=#888888>(Default: S)</color>"));
    }

    [Fact]
    public void IsWholeColorSpan_RejectsPlainAndEmptyText()
    {
        Assert.False(CaptionNote.IsWholeColorSpan("Plain caption"));
        Assert.False(CaptionNote.IsWholeColorSpan("<color=#888888></color>"));
        Assert.False(CaptionNote.IsWholeColorSpan(null));
    }

    // Two half-width column headers folded into one pair, then drawn again as headings.
    private static readonly LineBox Pair = Box(208f, 432f, 426f, 22f);

    [Fact]
    public void RedrawsPairSegment_MatchesEitherHalfInsideThePair()
    {
        Assert.True(CaptionNote.RedrawsPairSegment(Pair, "Loaded Tags: Saved Tags", "Loaded Tags", Box(208f, 432f, 213f, 22f), "Loaded Tags"));
        Assert.True(CaptionNote.RedrawsPairSegment(Pair, "Loaded Tags: Saved Tags", "Loaded Tags", Box(421f, 432f, 213f, 22f), "Saved Tags"));
    }

    [Fact]
    public void RedrawsPairSegment_RejectsOtherTextOrPlace()
    {
        Assert.False(CaptionNote.RedrawsPairSegment(Pair, "Loaded Tags: Saved Tags", "Loaded Tags", Box(208f, 432f, 213f, 22f), "Tags"));
        Assert.False(CaptionNote.RedrawsPairSegment(Pair, "Loaded Tags: Saved Tags", "Loaded Tags", Box(208f, 460f, 213f, 22f), "Loaded Tags"));
        Assert.False(CaptionNote.RedrawsPairSegment(Pair, "Loaded Tags: Saved Tags", "Loaded Tags", Box(700f, 432f, 213f, 22f), "Saved Tags"));
    }

    [Fact]
    public void InsideOnLine_AcceptsAnIconAtTheRowsEnd()
    {
        Assert.True(DetachedCaption.InsideOnLine(Box(220f, 266f, 821f, 30f), Box(997f, 273f, 16f, 16f)));
    }

    [Fact]
    public void InsideOnLine_RejectsAnIconOnAnotherLineOrOutside()
    {
        Assert.False(DetachedCaption.InsideOnLine(Box(220f, 266f, 821f, 30f), Box(997f, 300f, 16f, 16f)));
        Assert.False(DetachedCaption.InsideOnLine(Box(220f, 266f, 821f, 30f), Box(1050f, 273f, 16f, 16f)));
        Assert.False(DetachedCaption.InsideOnLine(Box(220f, 266f, 16f, 16f), Box(220f, 266f, 16f, 16f)));
    }

    [Fact]
    public void TrailsControl_PairsAnInfoIconWithTheNameRightOfIt()
    {
        Assert.True(DetachedCaption.TrailsControl(Box(213f, 1308f, 16f, 16f), Box(233f, 1306f, 338f, 22f)));
        Assert.True(DetachedCaption.TrailsControl(Box(233f, 1306f, 338f, 22f), Box(576f, 1306f, 150f, 22f)));
    }

    [Fact]
    public void IsMultiValueReadout_AcceptsCounterLines()
    {
        Assert.True(CaptionNote.IsMultiValueReadout("Chars: 0 | Est. Tokens (This Window): ~0"));
        Assert.True(CaptionNote.IsMultiValueReadout("ABM 0 · SCM 0 · ELS 0 · CLPA 0"));
        Assert.True(CaptionNote.IsMultiValueReadout("Selected: 0 traits (0 good, 0 bad)"));
        Assert.True(CaptionNote.IsMultiValueReadout("统计: 拦截对话 0 次 | 检出标签 0 次 | 执行成功 0 次"));
    }

    [Fact]
    public void IsMultiValueReadout_KeepsCaptionsWithOneOrTwoNumbers()
    {
        Assert.False(CaptionNote.IsMultiValueReadout("Max Level: 1600  (Default: 1600)"));
        Assert.False(CaptionNote.IsMultiValueReadout("Placeholders: {0}=Colonist Name, {1}=Memory List"));
        Assert.False(CaptionNote.IsMultiValueReadout("Trait Commonality: 24%"));
        Assert.False(CaptionNote.IsMultiValueReadout("Range: 1.5 - 2,000"));
        Assert.False(CaptionNote.IsMultiValueReadout("Prosthetic surgeries"));
        Assert.False(CaptionNote.IsMultiValueReadout(null));
    }

    [Fact]
    public void IsDimmedTint_GreyAndFadedOnly()
    {
        Assert.True(CaptionNote.IsDimmedTint(0.5f, 0.5f, 0.5f, 1f));
        Assert.True(CaptionNote.IsDimmedTint(0.6f, 0.6f, 0.6f, 0.7f));
        Assert.True(CaptionNote.IsDimmedTint(1f, 1f, 1f, 0.5f));
        Assert.False(CaptionNote.IsDimmedTint(1f, 1f, 1f, 1f));
        Assert.False(CaptionNote.IsDimmedTint(0.8f, 1f, 0.8f, 1f));
        Assert.False(CaptionNote.IsDimmedTint(0.8f, 0.9f, 1f, 1f));
        Assert.False(CaptionNote.IsDimmedTint(1f, 0.4f, 0.4f, 1f));
    }

    [Fact]
    public void SitsUnder_NoteDirectlyBelowAControl()
    {
        LineBox checkbox = Box(208f, 593f, 844f, 24f);
        Assert.True(CaptionNote.SitsUnder(checkbox, Box(208f, 619f, 844f, 14f)));
        Assert.True(CaptionNote.SitsUnder(Box(208f, 957f, 240f, 28f), Box(208f, 989f, 844f, 20f)));
        Assert.False(CaptionNote.SitsUnder(checkbox, Box(208f, 640f, 844f, 14f)));
        Assert.False(CaptionNote.SitsUnder(checkbox, Box(1100f, 619f, 200f, 14f)));
        Assert.False(CaptionNote.SitsUnder(checkbox, Box(208f, 560f, 844f, 14f)));
    }
}
