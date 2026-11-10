using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class BlankFieldCaptionTests
{
    private static LineBox Box(float x, float y, float width, float height)
    {
        return new LineBox { X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void IconMarksEnd_SearchIconLeftOrRightOfTheField()
    {
        LineBox quickSearchField = Box(434f, 140f, 280f, 24f);
        Assert.True(BlankFieldCaption.IconMarksEnd(quickSearchField, Box(412f, 143f, 18f, 18f)));
        LineBox xmlField = Box(200f, 100f, 300f, 22f);
        Assert.True(BlankFieldCaption.IconMarksEnd(xmlField, Box(500f, 100f, 22f, 22f)));
    }

    [Fact]
    public void IconMarksEnd_RejectsIconsOffTheFieldLineOrFarAway()
    {
        LineBox field = Box(200f, 100f, 300f, 22f);
        Assert.False(BlankFieldCaption.IconMarksEnd(field, Box(500f, 140f, 22f, 22f)));
        Assert.False(BlankFieldCaption.IconMarksEnd(field, Box(560f, 100f, 22f, 22f)));
        Assert.False(BlankFieldCaption.IconMarksEnd(field, Box(150f, 100f, 22f, 22f)));
        Assert.False(BlankFieldCaption.IconMarksEnd(field, Box(200f, 100f, 400f, 22f)));
    }

    [Fact]
    public void PlaceholderInside_CentreInsideTheField()
    {
        LineBox field = Box(200f, 100f, 300f, 22f);
        Assert.True(BlankFieldCaption.PlaceholderInside(field, Box(205f, 101f, 120f, 20f)));
        Assert.False(BlankFieldCaption.PlaceholderInside(field, Box(205f, 115f, 120f, 20f)));
        Assert.False(BlankFieldCaption.PlaceholderInside(field, Box(480f, 101f, 60f, 20f)));
    }

    [Fact]
    public void CaptionAbove_DirectlyAboveInTheFieldColumn()
    {
        LineBox field = Box(200f, 100f, 300f, 22f);
        Assert.True(BlankFieldCaption.CaptionAbove(field, Box(200f, 76f, 200f, 22f)));
        Assert.True(BlankFieldCaption.CaptionAbove(field, Box(200f, 60f, 200f, 22f)));
        Assert.False(BlankFieldCaption.CaptionAbove(field, Box(200f, 50f, 200f, 22f)));
        Assert.False(BlankFieldCaption.CaptionAbove(field, Box(520f, 76f, 200f, 22f)));
        Assert.False(BlankFieldCaption.CaptionAbove(field, Box(200f, 90f, 200f, 22f)));
    }
}
