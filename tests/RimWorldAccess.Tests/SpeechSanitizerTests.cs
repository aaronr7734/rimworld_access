using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="SpeechSanitizer.Sanitize"/>, the pipeline every screen
/// reader announcement passes through. A regression here would degrade output
/// for every user, in every language, so the rules are pinned explicitly.
/// </summary>
public class SpeechSanitizerTests
{
    [Fact]
    public void Null_PassesThrough()
    {
        Assert.Null(SpeechSanitizer.Sanitize(null!));
    }

    [Fact]
    public void Empty_PassesThrough()
    {
        Assert.Equal("", SpeechSanitizer.Sanitize(""));
    }

    [Fact]
    public void PlainText_Unchanged()
    {
        Assert.Equal("Build wall", SpeechSanitizer.Sanitize("Build wall"));
    }

    [Theory]
    [InlineData("<b>bold</b>", "bold")]
    [InlineData("<color=red>danger</color>", "danger")]
    [InlineData("a<b>b</b>c", "abc")]
    public void StripsMarkupTags(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void CollapsesRepeatedSpaces()
    {
        Assert.Equal("a b", SpeechSanitizer.Sanitize("a    b"));
    }

    [Theory]
    [InlineData("line1\nline2", "line1. line2")]
    [InlineData("a\r\nb", "a. b")]
    [InlineData("a\n\n\nb", "a. b")]
    public void FoldsNewlinesIntoSentenceBreaks(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void DropsRedundantPunctuationAfterNewline()
    {
        // A newline already acts as a sentence break; the following "." is dropped
        // rather than producing a stray spoken "period".
        Assert.Equal("a. b", SpeechSanitizer.Sanitize("a\n. b"));
    }

    [Fact]
    public void CollapsesSpaceBeforePunctuation()
    {
        // Built from "{label} . {value}" where the label resolved empty.
        Assert.Equal("Blindness. Horrible", SpeechSanitizer.Sanitize("Blindness . Horrible"));
    }

    [Fact]
    public void StripsLeadingOrphanPunctuation()
    {
        // Row built with a leading separator: ". Suppression: 50%".
        Assert.Equal("Suppression: 50%", SpeechSanitizer.Sanitize(". Suppression: 50%"));
    }

    [Fact]
    public void PreservesTrailingEllipsis()
    {
        Assert.Equal("Loading...", SpeechSanitizer.Sanitize("Loading..."));
    }

    [Fact]
    public void PreservesLeadingEllipsis()
    {
        // The ellipsis is masked before orphan-punctuation stripping precisely so
        // a legitimate leading "..." survives.
        Assert.Equal("...and more", SpeechSanitizer.Sanitize("...and more"));
    }

    [Theory]
    [InlineData("End..", "End.")]
    [InlineData("Done. . Next", "Done. Next")]
    public void CollapsesDoublePeriods(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("Label:. value", "Label. value")]
    [InlineData("a,. b", "a. b")]
    [InlineData("a,: b", "a: b")]
    public void FixesAdjacentPunctuation(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("hi", SpeechSanitizer.Sanitize("   hi   "));
    }
}
