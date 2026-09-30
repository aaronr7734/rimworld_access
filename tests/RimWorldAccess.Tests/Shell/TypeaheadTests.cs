using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TypeaheadMatcherTests
{
    [Fact]
    public void FirstWordMatches_RankAboveOtherWordAndDescription()
    {
        var labels = new List<string>
        {
            "5 wood. A stack of logs",     // 'w' matches "wood" as a later word -> OtherWord
            "Wall: 5 wood. Blocks movement", // FirstWord
            "Table. Eat with wooden dishes", // 'w' only in description -> Description
        };

        var matches = TypeaheadMatcher.FindMatches("w", labels);

        Assert.Equal(new[] { 1, 0, 2 }, matches);
    }

    [Fact]
    public void WithinTier_ShorterNamesWin()
    {
        var labels = new List<string> { "Wall lamp", "Wall", "Wallpaper maker" };
        Assert.Equal(new[] { 1, 0, 2 }, TypeaheadMatcher.FindMatches("wall", labels));
    }

    [Fact]
    public void ExactName_WinsOutright()
    {
        // The architect tree's real shape: the bare wall's label carries vanilla's
        // "choose a material" ellipsis, which is separators only — still an exact name.
        var labels = new List<string>
        {
            "Wall lamp: 15 steel. A wall-mounted lamp",
            "Wall torch lamp: 15 wood. A wall-mounted torch",
            "Wall...: 5 material. An impassable wall",
        };
        Assert.Equal(new[] { 2, 0, 1 }, TypeaheadMatcher.FindMatches("wall", labels));
    }

    [Fact]
    public void ExactName_OutranksInteractability()
    {
        var labels = new List<string> { "Wall lamp", "Wall" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text),
        };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("wall", labels, candidates));
    }

    [Fact]
    public void WholeWord_BeatsPartialWordPrefix()
    {
        // "wall" is a complete word of "Wooden wall" but only part of "Wallpaper";
        // where the complete word sits in the name does not matter.
        var labels = new List<string> { "Wallpaper maker", "Wooden wall" };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("wall", labels));
    }

    [Fact]
    public void WholeWord_FewerWordsWinBeforeLength()
    {
        // The longer name wins on word count: it is the less qualified of the two.
        var labels = new List<string> { "Old red wall", "Cryptosleep wall" };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("wall", labels));
    }

    [Fact]
    public void ArchitectWallScenario_RanksTheWallAheadOfWallFittings()
    {
        var labels = new List<string> { "Wall lamp", "Wall torch lamp", "Wooden wall", "Wall" };
        Assert.Equal(new[] { 3, 0, 2, 1 }, TypeaheadMatcher.FindMatches("wall", labels));
    }

    [Fact]
    public void StableIdentity_KeepsTheWinnerWhenTheGameRelabelsTheRow()
    {
        // The reported regression: one architect row, relabelled by vanilla the moment its
        // material menu opened, dropped from first to third for the same query. Only that
        // one label differs between the two corpora.
        var beforeMaterialPicked = new List<string>
        {
            "Wall lamp: 15 steel. A wall-mounted lamp",
            "Wall torch lamp: 15 wood. A wall-mounted torch",
            "Wall...: 5 material. An impassable wall",
        };
        var afterMaterialPicked = new List<string>(beforeMaterialPicked);
        afterMaterialPicked[2] = "Wooden wall: 5 material. An impassable wall";

        // Every row's identity is its def label, which vanilla never rewrites.
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "wall lamp"),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "wall torch lamp"),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "wall"),
        };

        Assert.Equal(
            TypeaheadMatcher.FindMatches("wall", beforeMaterialPicked, candidates),
            TypeaheadMatcher.FindMatches("wall", afterMaterialPicked, candidates));
        Assert.Equal(2, TypeaheadMatcher.FindMatches("wall", afterMaterialPicked, candidates)[0]);
    }

    [Fact]
    public void StableIdentity_NeverChangesTheMatchSet()
    {
        // The identity orders results; it never admits a row the player's text cannot
        // reach on screen, so the match count they hear stays honest.
        var labels = new List<string> { "Steel", "Wooden wall" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "wall"),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "wall"),
        };

        Assert.Equal(new[] { 1 }, TypeaheadMatcher.FindMatches("wall", labels, candidates));
    }

    [Fact]
    public void StableIdentity_DoesNotDemoteTheRowThatOwnsIt()
    {
        // An identity the query misses leaves the displayed label's own ranking alone.
        var labels = new List<string> { "Wall lamp", "Wall" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "sconce"),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, "sconce"),
        };

        Assert.Equal(
            TypeaheadMatcher.FindMatches("wall", labels),
            TypeaheadMatcher.FindMatches("wall", labels, candidates));
    }

    [Fact]
    public void Ranking_IsIndependentOfCorpusOrder()
    {
        // Every key below the region key comes from the label alone, so reversing the
        // corpus reverses nothing but the ties.
        var labels = new List<string> { "Wall", "Wall lamp", "Wooden wall", "Wallpaper maker" };
        var reversed = new List<string> { "Wallpaper maker", "Wooden wall", "Wall lamp", "Wall" };

        Assert.Equal(new[] { 0, 1, 2, 3 }, TypeaheadMatcher.FindMatches("wall", labels));
        Assert.Equal(new[] { 3, 2, 1, 0 }, TypeaheadMatcher.FindMatches("wall", reversed));
    }

    [Fact]
    public void Multiword_ExactName_WinsOutright()
    {
        var labels = new List<string> { "Left arm brace", "Left arm" };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("left arm", labels));
    }

    [Fact]
    public void Multiword_WholeWords_BeatPartialPrefixes()
    {
        var labels = new List<string> { "Gas pipeline valve", "Steel gas pipe" };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("gas pipe", labels));
    }

    [Fact]
    public void Matching_IsDiacriticInsensitive()
    {
        var labels = new List<string> { "Café corner" };
        Assert.Equal(new[] { 0 }, TypeaheadMatcher.FindMatches("cafe", labels));
    }

    [Fact]
    public void ParentheticalContent_DoesNotPolluteNameTiers()
    {
        var labels = new List<string>
        {
            "Sleeping spot (wood floor)",   // 'w' inside parens only -> Description tier
            "Wood-fired generator",         // FirstWord
        };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("w", labels));
    }

    [Fact]
    public void NoMatch_YieldsEmpty()
    {
        Assert.Empty(TypeaheadMatcher.FindMatches("zzz", new List<string> { "Wall", "Door" }));
        Assert.Empty(TypeaheadMatcher.FindMatches("", new List<string> { "Wall" }));
        Assert.Empty(TypeaheadMatcher.FindMatches("w", null));
    }

    [Fact]
    public void Multiword_LiteralPhrase_MatchesAtNameStart()
    {
        var labels = new List<string> { "Left arm", "Right arm", "Left leg" };
        Assert.Equal(new[] { 0 }, TypeaheadMatcher.FindMatches("left arm", labels));
    }

    [Fact]
    public void Multiword_OrderedTokens_MatchWordPrefixesWithGaps()
    {
        var labels = new List<string> { "Gas pipe", "Gas metal pipe", "Pipe gasket" };
        // "ga pi": both labels whose words fit the tokens in order; "Pipe gasket" has them reversed.
        Assert.Equal(new[] { 0, 1 }, TypeaheadMatcher.FindMatches("ga pi", labels));
    }

    [Fact]
    public void Multiword_TokenOrder_IsEnforced()
    {
        var labels = new List<string> { "Gas pipe" };
        Assert.Empty(TypeaheadMatcher.FindMatches("pi ga", labels));
    }

    [Fact]
    public void Multiword_TrailingSpace_KeepsCurrentMatches()
    {
        var labels = new List<string> { "Left arm", "Left leg", "Door" };
        Assert.Equal(
            TypeaheadMatcher.FindMatches("left", labels),
            TypeaheadMatcher.FindMatches("left ", labels));
    }

    [Fact]
    public void Multiword_FirstWordStart_RanksAboveLaterWordStart()
    {
        var labels = new List<string>
        {
            "Toggle hidden conditions display", // tokens land from word 1 -> OtherWord
            "Hidden conditions",                // tokens land from word 0 -> FirstWord
        };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("hi cond", labels));
    }

    [Fact]
    public void AcceptsSearchChar_SpaceOnlyDuringActiveSearch()
    {
        Assert.False(TypeaheadMatcher.AcceptsSearchChar(' ', searchActive: false));
        Assert.True(TypeaheadMatcher.AcceptsSearchChar(' ', searchActive: true));
        Assert.True(TypeaheadMatcher.AcceptsSearchChar('a', searchActive: false));
        Assert.True(TypeaheadMatcher.AcceptsSearchChar('5', searchActive: false));
        Assert.False(TypeaheadMatcher.AcceptsSearchChar('5', searchActive: false, acceptDigits: false));
        Assert.False(TypeaheadMatcher.AcceptsSearchChar('.', searchActive: true));
    }

    [Fact]
    public void NoCandidates_PreservesLegacyTierOrder()
    {
        var labels = new List<string>
        {
            "5 wood. A stack of logs",     // OtherWord
            "Wall: 5 wood. Blocks movement", // FirstWord
            "Table. Eat with wooden dishes", // Description
        };

        var matches = TypeaheadMatcher.FindMatches("w", labels, null);

        Assert.Equal(new[] { 1, 0, 2 }, matches);
    }

    [Fact]
    public void PriorityGroup_RanksAboveEverythingElse()
    {
        var labels = new List<string> { "Table. Eat with wooden dishes", "Wall: 5 wood. Blocks movement" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(true, TypeaheadCandidateKind.Item),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item),
        };

        var matches = TypeaheadMatcher.FindMatches("w", labels, candidates);

        Assert.Equal(new[] { 0, 1 }, matches);
    }

    [Fact]
    public void Band_DemotesBelowEveryOtherKeyButPriority()
    {
        var labels = new List<string> { "Tame", "Tameness training" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text, null, 1),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control, null, 0),
        };

        var matches = TypeaheadMatcher.FindMatches("tame", labels, candidates);

        Assert.Equal(new[] { 1, 0 }, matches);
    }

    [Fact]
    public void Priority_BeatsBand()
    {
        var labels = new List<string> { "Bob", "Bob" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item, null, 0),
            new TypeaheadCandidate(true, TypeaheadCandidateKind.Text, null, 1),
        };

        var matches = TypeaheadMatcher.FindMatches("bob", labels, candidates);

        Assert.Equal(new[] { 1, 0 }, matches);
    }

    [Fact]
    public void NameMatch_BeatsInteractability()
    {
        var labels = new List<string> { "Enable mod. Covers a wall feature", "Wall: 5 wood. Blocks movement" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text),
        };

        var matches = TypeaheadMatcher.FindMatches("wall", labels, candidates);

        Assert.Equal(new[] { 1, 0 }, matches);
    }

    [Fact]
    public void Interactability_BeatsFineTier()
    {
        var labels = new List<string>
        {
            "Small wall",                    // OtherWord, Control
            "Wall lamp",                     // FirstWord, Text
            "Wall socket",                   // FirstWord, Item
        };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item),
        };

        var matches = TypeaheadMatcher.FindMatches("wa", labels, candidates);

        Assert.Equal(new[] { 0, 2, 1 }, matches);
    }

    [Fact]
    public void ItemRank_SitsBetweenControlAndText()
    {
        var labels = new List<string> { "Wall text", "Wall item", "Wall control" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
        };

        var matches = TypeaheadMatcher.FindMatches("wall", labels, candidates);

        Assert.Equal(new[] { 2, 1, 0 }, matches);
    }

    [Fact]
    public void ModMenuShape_ControlsOutrankDescriptionProse()
    {
        var labels = new List<string>
        {
            "RimHUD",
            "Rim options",
            "Mod details. This mod adds a HUD for RimWorld players",
        };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Control),
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Text),
        };

        var matches = TypeaheadMatcher.FindMatches("rim", labels, candidates);

        // Within the tied Control rank, "Rim" is a complete word of "Rim options" but only
        // part of "RimHUD", and the whole-word tier outranks the shorter name.
        Assert.Equal(new[] { 1, 0, 2 }, matches);
    }

    [Fact]
    public void SubstringFallback_Default_RefusesMidWord()
    {
        var labels = new List<string> { "Grizzly bear" };
        Assert.Empty(TypeaheadMatcher.FindMatches("rizz", labels));
    }

    [Fact]
    public void SubstringFallback_OptIn_AdmitsMidWord()
    {
        var labels = new List<string> { "Grizzly bear" };
        Assert.Equal(new[] { 0 }, TypeaheadMatcher.FindMatches("rizz", labels, null, substringFallback: true));
    }

    [Fact]
    public void SubstringFallback_WordPrefixStillOutranksSubstring()
    {
        var labels = new List<string> { "Alphabeaver", "Bear" };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("bea", labels, null, substringFallback: true));
    }

    [Fact]
    public void DocumentOrder_KeepsRowOrderOverQuality()
    {
        var labels = new List<string> { "Wallpaper maker", "Bob smacks Wall", "Wall" };
        Assert.Equal(new[] { 0, 1, 2 }, TypeaheadMatcher.FindMatches("wall", labels, null, false, documentOrder: true));
    }

    [Fact]
    public void DocumentOrder_CurrentRegionStillFirst()
    {
        var labels = new List<string> { "Wall", "Wall lamp" };
        var candidates = new List<TypeaheadCandidate>
        {
            new TypeaheadCandidate(false, TypeaheadCandidateKind.Item),
            new TypeaheadCandidate(true, TypeaheadCandidateKind.Item),
        };
        Assert.Equal(new[] { 1, 0 }, TypeaheadMatcher.FindMatches("wall", labels, candidates, false, documentOrder: true));
    }

    [Fact]
    public void SubstringFallback_MultiwordQuery_ReachesFallback()
    {
        var labels = new List<string> { "Megasloth wool" };
        Assert.Equal(new[] { 0 }, TypeaheadMatcher.FindMatches("aslo wo", labels, null, substringFallback: true));
        Assert.Empty(TypeaheadMatcher.FindMatches("aslo wo", labels));
    }

    [Fact]
    public void SubstringFallback_MultiwordQueryWithSeparatorInToken_ReachesLiteralFallback()
    {
        // "b-c" fits no single word once separators split the label, but the
        // whole label contains the literal query — a Contains-filtered dialog
        // draws this row, so the matcher must reach it.
        var labels = new List<string> { "xa b-c y" };
        Assert.Equal(new[] { 0 }, TypeaheadMatcher.FindMatches("a b-c", labels, null, substringFallback: true));
        Assert.Empty(TypeaheadMatcher.FindMatches("a b-c", labels));
    }

    [Theory]
    [InlineData(ElementRole.Button, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.Checkbox, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.ComboBox, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.Slider, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.Stepper, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.RadioButton, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.TextField, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.Tab, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.Map, false, TypeaheadCandidateKind.Control)]
    [InlineData(ElementRole.None, false, TypeaheadCandidateKind.Item)]
    [InlineData(ElementRole.MenuItem, false, TypeaheadCandidateKind.Item)]
    [InlineData(ElementRole.TreeItem, false, TypeaheadCandidateKind.Item)]
    [InlineData(ElementRole.TableCell, false, TypeaheadCandidateKind.Item)]
    [InlineData(ElementRole.Button, true, TypeaheadCandidateKind.Text)]
    [InlineData(ElementRole.None, true, TypeaheadCandidateKind.Text)]
    public void ClassifyRow_MapsRolesAndReadOnly(ElementRole role, bool readOnly, TypeaheadCandidateKind expected)
    {
        Assert.Equal(expected, TypeaheadMatcher.ClassifyRow(role, readOnly));
    }
}

public class TypeaheadModelTests
{
    private static readonly List<string> Labels = new() { "Wall", "Door", "Wall lamp", "Bed" };

    [Fact]
    public void Append_BuildsBufferAndLandsOnBestMatch()
    {
        var model = new TypeaheadModel();

        Assert.True(model.Append('w', Labels, 0.0, out int index));
        Assert.Equal(0, index); // "Wall" beats "Wall lamp" (shorter)
        Assert.Equal("w", model.Buffer);
        Assert.Equal(2, model.MatchCount);
        Assert.Equal(1, model.CurrentMatchPosition);
    }

    [Fact]
    public void Append_NoMatch_RemembersFailureAndAutoClears()
    {
        var model = new TypeaheadModel();

        Assert.False(model.Append('z', Labels, 0.0, out int index));
        Assert.Equal(-1, index);
        Assert.False(model.HasActiveSearch);
        Assert.Equal("z", model.LastFailedSearch);
    }

    [Fact]
    public void Append_AfterTimeout_StartsFreshQuery()
    {
        var model = new TypeaheadModel();
        model.Append('w', Labels, 0.0, out _);

        // 4 seconds later: past the 3-second auto-reset, 'd' starts a new search.
        Assert.True(model.Append('d', Labels, 4.0, out int index));
        Assert.Equal("d", model.Buffer);
        Assert.Equal(1, index); // "Door"
    }

    [Fact]
    public void Append_WithinTimeout_ExtendsQuery()
    {
        var model = new TypeaheadModel();
        model.Append('w', Labels, 0.0, out _);

        Assert.True(model.Append('a', Labels, 1.0, out int index));
        Assert.Equal("wa", model.Buffer);
        Assert.Equal(0, index);
    }

    [Fact]
    public void SuppressAutoReset_KeepsSlowIMEQueriesAlive()
    {
        var model = new TypeaheadModel { SuppressAutoReset = true };
        model.Append('w', Labels, 0.0, out _);

        Assert.True(model.Append('a', Labels, 60.0, out _));
        Assert.Equal("wa", model.Buffer);
    }

    [Fact]
    public void Backspace_ShrinksBufferThenClears()
    {
        var model = new TypeaheadModel();
        model.Append('w', Labels, 0.0, out _);
        model.Append('a', Labels, 0.5, out _);

        Assert.True(model.Backspace(Labels, 1.0, out int index));
        Assert.Equal("w", model.Buffer);
        Assert.Equal(0, index);

        Assert.True(model.Backspace(Labels, 1.5, out _));
        Assert.False(model.HasActiveSearch);

        Assert.False(model.Backspace(Labels, 2.0, out _));
    }

    [Fact]
    public void NextAndPreviousMatch_CycleThroughMatchesWithWrap()
    {
        var model = new TypeaheadModel();
        model.Append('w', Labels, 0.0, out int first);
        Assert.Equal(0, first); // matches: Wall(0), Wall lamp(2)

        Assert.Equal(2, model.NextMatch(0));
        Assert.Equal(2, model.CurrentMatchPosition);
        Assert.Equal(0, model.NextMatch(2)); // wraps
        Assert.Equal(2, model.PreviousMatch(0)); // wraps back
        Assert.Equal(0, model.FirstMatch());
        Assert.Equal(2, model.LastMatch());
    }

    [Fact]
    public void NextMatch_FromNonMatchIndex_FindsFollowingMatch()
    {
        var model = new TypeaheadModel();
        model.Append('w', Labels, 0.0, out _); // matches 0 and 2

        Assert.Equal(2, model.NextMatch(1));
        Assert.Equal(0, model.PreviousMatch(1));
    }

    [Fact]
    public void SubstringFallback_KeepsBufferAliveForMidWordQuery()
    {
        var labels = new List<string> { "Grizzly bear" };
        var model = new TypeaheadModel { SubstringFallback = true };

        model.Append('r', labels, 0.0, out _);
        model.Append('i', labels, 0.1, out _);
        model.Append('z', labels, 0.2, out _);
        model.Append('z', labels, 0.3, out _);

        Assert.True(model.HasActiveSearch);
        Assert.Equal(1, model.MatchCount);

        var defaultModel = new TypeaheadModel();
        defaultModel.Append('r', labels, 0.0, out _);
        defaultModel.Append('i', labels, 0.1, out _);
        defaultModel.Append('z', labels, 0.2, out _);
        defaultModel.Append('z', labels, 0.3, out _);

        Assert.False(defaultModel.HasActiveSearch);
        Assert.Equal("z", defaultModel.LastFailedSearch); // fails and resets every keystroke: no word-prefix survives 'r' alone
    }
}
