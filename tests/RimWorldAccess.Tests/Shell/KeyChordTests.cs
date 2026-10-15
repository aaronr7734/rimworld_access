using System.Collections.Generic;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess.Tests.Shell;

public class KeyChordTests
{
    [Theory]
    [InlineData("Alt+M")]
    [InlineData("Ctrl+Shift+G")]
    [InlineData("Ctrl+Shift+Alt+F5")]
    [InlineData("UpArrow")]
    [InlineData("Shift+F12")]
    [InlineData("Ctrl+KeypadPlus")]
    [InlineData("Alpha3")]
    public void Serialize_RoundTripsThroughParse(string canonical)
    {
        var chord = KeyChord.Parse(canonical);
        Assert.Equal(canonical, chord.Serialize());
        Assert.Equal(chord, KeyChord.Parse(chord.Serialize()));
    }

    [Fact]
    public void Parse_AcceptsAnyModifierOrderAndCase()
    {
        var chord = KeyChord.Parse("alt+SHIFT+m");
        Assert.Equal(KeyCode.M, chord.Key);
        Assert.True(chord.Shift);
        Assert.True(chord.Alt);
        Assert.False(chord.Ctrl);
        Assert.Equal("Shift+Alt+M", chord.Serialize());
    }

    [Fact]
    public void Parse_AcceptsControlAsCtrlAlias()
    {
        var chord = KeyChord.Parse("Control+K");
        Assert.True(chord.Ctrl);
        Assert.Equal(KeyCode.K, chord.Key);
        Assert.Equal("Ctrl+K", chord.Serialize());
    }

    [Theory]
    [InlineData("0", KeyCode.Alpha0)]
    [InlineData("5", KeyCode.Alpha5)]
    [InlineData("9", KeyCode.Alpha9)]
    public void Parse_MapsBareDigitToTopRowNumberKey(string token, KeyCode expected)
    {
        Assert.Equal(expected, KeyChord.Parse(token).Key);
        Assert.Equal(expected, KeyChord.Parse("Ctrl+Alt+" + token).Key);
    }

    [Theory]
    [InlineData("53")]
    [InlineData("Ctrl+53")]
    public void TryParse_RejectsMultiDigitNumericToken(string text)
    {
        // Enum.Parse would otherwise cast "53" to (KeyCode)53 (=Alpha5); a
        // numeric token that is not a single digit must be rejected, not cast.
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("+M")]
    [InlineData("NotAKey")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("M+Ctrl")]
    [InlineData("None")]
    [InlineData("Ctrl+None")]
    public void TryParse_RejectsMalformedInput(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Fact]
    public void Constructor_RejectsKeyCodeNone()
    {
        Assert.Throws<System.ArgumentException>(() => new KeyChord(KeyCode.None));
    }

    [Fact]
    public void Matches_RequiresExactModifierEquality()
    {
        var altM = KeyChord.Of(KeyCode.M, alt: true);

        Assert.True(altM.Matches(new KeyEventSnapshot(KeyCode.M, alt: true)));
        Assert.False(altM.Matches(new KeyEventSnapshot(KeyCode.M)));
        Assert.False(altM.Matches(new KeyEventSnapshot(KeyCode.M, alt: true, shift: true)));
        Assert.False(altM.Matches(new KeyEventSnapshot(KeyCode.M, ctrl: true, alt: true)));
        Assert.False(altM.Matches(new KeyEventSnapshot(KeyCode.N, alt: true)));
    }

    [Fact]
    public void Matches_BareKeyRejectsHeldModifiers()
    {
        var plainM = KeyChord.Of(KeyCode.M);
        Assert.True(plainM.Matches(new KeyEventSnapshot(KeyCode.M)));
        Assert.False(plainM.Matches(new KeyEventSnapshot(KeyCode.M, alt: true)));
        Assert.False(plainM.Matches(new KeyEventSnapshot(KeyCode.M, shift: true)));
    }

    [Fact]
    public void Snapshot_FromChord_MatchesItsChord()
    {
        var chord = KeyChord.Parse("Ctrl+Shift+Home");
        Assert.True(chord.Matches(KeyEventSnapshot.FromChord(chord)));
    }

    [Theory]
    [InlineData("Alt+UpArrow", "Alt+Up Arrow")]
    [InlineData("Return", "Enter")]
    [InlineData("Alpha3", "3")]
    [InlineData("Keypad5", "Numpad 5")]
    [InlineData("F5", "F5")]
    [InlineData("CapsLock", "Caps Lock")]
    [InlineData("Shift+PageDown", "Shift+Page Down")]
    public void DisplayLabel_HumanizesKeyNames(string serialized, string expected)
    {
        Assert.Equal(expected, KeyChord.Parse(serialized).DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_UsesInstalledModifierProviders()
    {
        var previous = KeyChordFormat.CtrlLabel;
        try
        {
            KeyChordFormat.CtrlLabel = key => key == KeyCode.Tab ? "Option" : "Command";
            Assert.Equal("Command+M", KeyChord.Parse("Ctrl+M").DisplayLabel);
            Assert.Equal("Option+Tab", KeyChord.Parse("Ctrl+Tab").DisplayLabel);
        }
        finally
        {
            KeyChordFormat.CtrlLabel = previous;
        }
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = KeyChord.Parse("Ctrl+Shift+G");
        var b = KeyChord.Of(KeyCode.G, ctrl: true, shift: true);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, KeyChord.Of(KeyCode.G, ctrl: true));
    }

    [Fact]
    public void ModifierHeldVariants_ReturnsSixDistinctChordsExcludingBareAndCtrlAlt()
    {
        var variants = KeyChord.ModifierHeldVariants(KeyCode.Return);

        Assert.Equal(6, variants.Count);
        Assert.All(variants, v => Assert.Equal(KeyCode.Return, v.Key));
        Assert.DoesNotContain(new KeyChord(KeyCode.Return), variants);
        Assert.DoesNotContain(new KeyChord(KeyCode.Return, ctrl: true, alt: true), variants);
        Assert.Equal(variants.Count, new HashSet<KeyChord>(variants).Count);
    }
}
