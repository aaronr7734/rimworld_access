using System.Collections.Generic;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess.Tests.Shell;

/// <summary>Records every character offered to it; HandleChar's return value is caller-supplied.</summary>
internal sealed class RecordingCharSink : ICharSink
{
    private readonly Func<char, bool> handle;
    public List<char> Received { get; } = new List<char>();

    public RecordingCharSink(Func<char, bool> handle)
    {
        this.handle = handle;
    }

    public bool HandleChar(char c)
    {
        Received.Add(c);
        return handle(c);
    }
}

public class FocusDispatchTests
{
    private static InputAction Screen(string scopeKey, string id, params string[] chords)
    {
        var defaults = new List<KeyChord>();
        foreach (var c in chords)
            defaults.Add(KeyChord.Parse(c));
        return InputAction.ForScreen(scopeKey, id, defaults);
    }

    [Fact]
    public void Dispatch_LiveScopeClaim_ConsumesMatchingSnapshot()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal);
        core.Push(scope);
        catalog.Register(Screen("scope", "scope.action", "Alt+M"));

        KeyEventSnapshot received = default;
        bool fired = false;
        scope.AddClaim("scope.action", e => { received = e; fired = true; });

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("scope.action", consumedId);
        Assert.True(fired);
        Assert.Equal(KeyCode.M, received.Key);
        Assert.True(received.Alt);
    }

    [Fact]
    public void Dispatch_ShadowScopeClaim_IsIgnored_CoexistenceGuarantee()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var shadow = new RecordingScope("shadow", journal, isLive: false);
        core.Push(shadow);
        catalog.Register(Screen("shadow", "shadow.action", "Alt+M"));

        bool fired = false;
        shadow.AddClaim("shadow.action", _ => fired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
        Assert.False(fired);
    }

    [Fact]
    public void Dispatch_NonMatchingChord_FallsThrough()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal);
        core.Push(scope);
        catalog.Register(Screen("scope", "scope.action", "Alt+M"));
        scope.AddClaim("scope.action");

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.N), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
    }

    [Fact]
    public void Dispatch_TopDownPrecedence_UpperScopeWinsOnSharedChord()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upper = new RecordingScope("upper", journal);
        core.Push(lower);
        core.Push(upper);
        catalog.Register(Screen("lower", "lower.action", "Alt+M"));
        catalog.Register(Screen("upper", "upper.action", "Alt+M"));

        bool lowerFired = false, upperFired = false;
        lower.AddClaim("lower.action", _ => lowerFired = true);
        upper.AddClaim("upper.action", _ => upperFired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("upper.action", consumedId);
        Assert.True(upperFired);
        Assert.False(lowerFired);
    }

    [Fact]
    public void Dispatch_WhenGuardFalse_ModalScope_ReturnsFalse()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal); // modal by default
        core.Push(scope);
        catalog.Register(Screen("scope", "scope.action", "Alt+M"));

        bool fired = false;
        scope.AddClaim("scope.action", _ => fired = true, when: () => false);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
        Assert.False(fired);
    }

    [Fact]
    public void Dispatch_WhenGuardFalse_NonModalScope_FallsThroughToLowerClaim()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upper = new RecordingScope("upper", journal, isModal: false);
        core.Push(lower);
        core.Push(upper);
        catalog.Register(Screen("upper", "upper.action", "Alt+M"));
        catalog.Register(Screen("lower", "lower.action", "Alt+M"));

        bool upperFired = false, lowerFired = false;
        upper.AddClaim("upper.action", _ => upperFired = true, when: () => false);
        lower.AddClaim("lower.action", _ => lowerFired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("lower.action", consumedId);
        Assert.False(upperFired);
        Assert.True(lowerFired);
    }

    [Fact]
    public void Dispatch_SameScopeOppositeWhenGuards_EligibleClaimFires()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal);
        core.Push(scope);
        catalog.Register(Screen("scope", "scope.gridMode", "Alt+M"));
        catalog.Register(Screen("scope", "scope.areaMode", "Alt+M"));

        bool gridActive = false;
        bool gridFired = false, areaFired = false;
        scope.AddClaim("scope.gridMode", _ => gridFired = true, when: () => gridActive);
        scope.AddClaim("scope.areaMode", _ => areaFired = true, when: () => !gridActive);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("scope.areaMode", consumedId);
        Assert.False(gridFired);
        Assert.True(areaFired);
    }

    [Fact]
    public void Dispatch_PropagateClaim_RunsHandlerAndContinuesToLowerConsumingClaim()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upper = new RecordingScope("upper", journal, isModal: false);
        core.Push(lower);
        core.Push(upper);
        catalog.Register(Screen("upper", "upper.observe", "Alt+M"));
        catalog.Register(Screen("lower", "lower.consume", "Alt+M"));

        bool upperFired = false, lowerFired = false;
        upper.AddClaim("upper.observe", _ => upperFired = true, propagate: true);
        lower.AddClaim("lower.consume", _ => lowerFired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("lower.consume", consumedId);
        Assert.True(upperFired);
        Assert.True(lowerFired);
    }

    [Fact]
    public void Dispatch_OnlyPropagateClaimsMatch_ReturnsFalseDespiteHandlersRunning()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal, isModal: false);
        core.Push(scope);
        catalog.Register(Screen("scope", "scope.observe", "Alt+M"));

        bool fired = false;
        scope.AddClaim("scope.observe", _ => fired = true, propagate: true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
        Assert.True(fired); // handler ran even though nothing consumed
    }

    [Fact]
    public void Dispatch_ModalTopScope_MasksLowerClaim_NoPassthrough()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upperModal = new RecordingScope("upperModal", journal); // no claim on this chord, no passthrough
        core.Push(lower);
        core.Push(upperModal);
        catalog.Register(Screen("lower", "lower.action", "Alt+M"));

        bool lowerFired = false;
        lower.AddClaim("lower.action", _ => lowerFired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
        Assert.False(lowerFired);
    }

    [Fact]
    public void Dispatch_Passthrough_AllowsListedActionThroughModal_MasksOthers()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upperModal = new RecordingScope("upperModal", journal);
        core.Push(lower);
        core.Push(upperModal);
        catalog.Register(Screen("lower", "lower.allowed", "Alt+M"));
        catalog.Register(Screen("lower", "lower.blocked", "Alt+N"));
        upperModal.AddPass("lower.allowed");

        bool allowedFired = false, blockedFired = false;
        lower.AddClaim("lower.allowed", _ => allowedFired = true);
        lower.AddClaim("lower.blocked", _ => blockedFired = true);

        bool consumedAllowed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string idAllowed);
        Assert.True(consumedAllowed);
        Assert.Equal("lower.allowed", idAllowed);
        Assert.True(allowedFired);

        bool consumedBlocked = core.Dispatch(new KeyEventSnapshot(KeyCode.N, alt: true), catalog, out string idBlocked);
        Assert.False(consumedBlocked);
        Assert.Null(idBlocked);
        Assert.False(blockedFired);
    }

    [Fact]
    public void Dispatch_Passthrough_StackedModals_RequireIntersectionOfPasslists()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var bottom = new RecordingScope("bottom", journal);
        var middleModal = new RecordingScope("middleModal", journal);
        var topModal = new RecordingScope("topModal", journal);
        core.Push(bottom);
        core.Push(middleModal);
        core.Push(topModal);

        catalog.Register(Screen("bottom", "bottom.onlyTopPasses", "Alt+M"));
        catalog.Register(Screen("bottom", "bottom.bothPass", "Alt+N"));

        // topModal passes both ids; middleModal only passes "bottom.bothPass", so the
        // effective intersection is {"bottom.bothPass"} - only that one reaches bottom.
        topModal.AddPass("bottom.onlyTopPasses");
        topModal.AddPass("bottom.bothPass");
        middleModal.AddPass("bottom.bothPass");

        bool onlyTopFired = false, bothPassFired = false;
        bottom.AddClaim("bottom.onlyTopPasses", _ => onlyTopFired = true);
        bottom.AddClaim("bottom.bothPass", _ => bothPassFired = true);

        bool consumedBlocked = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string idBlocked);
        Assert.False(consumedBlocked);
        Assert.Null(idBlocked);
        Assert.False(onlyTopFired);

        bool consumedThrough = core.Dispatch(new KeyEventSnapshot(KeyCode.N, alt: true), catalog, out string idThrough);
        Assert.True(consumedThrough);
        Assert.Equal("bottom.bothPass", idThrough);
        Assert.True(bothPassFired);
    }

    [Fact]
    public void Dispatch_LiveNonModalScope_DoesNotMaskLowerClaim()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var upperNonModal = new RecordingScope("upperNonModal", journal, isModal: false);
        core.Push(lower);
        core.Push(upperNonModal);
        catalog.Register(Screen("lower", "lower.action", "Alt+M"));

        bool lowerFired = false;
        lower.AddClaim("lower.action", _ => lowerFired = true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("lower.action", consumedId);
        Assert.True(lowerFired);
    }

    [Fact]
    public void Dispatch_ClaimOnUnregisteredAction_Throws()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog(); // "ghost.action" is deliberately never registered
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal);
        core.Push(scope);
        scope.AddClaim("ghost.action");

        var ex = Assert.Throws<System.InvalidOperationException>(
            () => core.Dispatch(new KeyEventSnapshot(KeyCode.M), catalog, out _));

        Assert.Contains("scope", ex.Message);
        Assert.Contains("ghost.action", ex.Message);
    }

    [Fact]
    public void Dispatch_HandlerPushesScopeMidDispatch_WalkContinuesSafely()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal, isModal: false);
        var pushedMidDispatch = new RecordingScope("pushedMidDispatch", journal);
        core.Push(a);
        core.Push(b);

        catalog.Register(Screen("b", "b.pushNew", "Alt+M"));
        catalog.Register(Screen("a", "a.fallback", "Alt+M"));

        bool aFired = false;
        a.AddClaim("a.fallback", _ => aFired = true);
        b.AddClaim("b.pushNew", _ => core.Push(pushedMidDispatch), propagate: true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.True(consumed);
        Assert.Equal("a.fallback", consumedId);
        Assert.True(aFired);
        Assert.Same(pushedMidDispatch, core.Top); // the mid-dispatch push landed on the real stack
        Assert.Equal(3, core.Count);
    }

    [Fact]
    public void Dispatch_HandlerPopsLowerScopeMidDispatch_PoppedScopesClaimIsSkipped()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        var c = new RecordingScope("c", journal, isModal: false);
        core.Push(a);
        core.Push(b);
        core.Push(c);

        catalog.Register(Screen("c", "c.popLower", "Alt+M"));
        catalog.Register(Screen("b", "b.claim", "Alt+M"));

        bool bFired = false;
        b.AddClaim("b.claim", _ => bFired = true);
        c.AddClaim("c.popLower", _ => core.Pop(b), propagate: true);

        bool consumed = core.Dispatch(new KeyEventSnapshot(KeyCode.M, alt: true), catalog, out string consumedId);

        Assert.False(consumed);
        Assert.Null(consumedId);
        Assert.False(bFired);
        Assert.DoesNotContain(b, core.ScopesBottomUp);
        Assert.Equal(2, core.Count); // a and c remain
    }

    [Fact]
    public void OfferChar_LiveScopeWithConsumingSink_ReturnsTrue()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal);
        var sink = new RecordingCharSink(_ => true);
        scope.CharSinkOverride = sink;
        core.Push(scope);

        Assert.True(core.OfferChar('a'));
        Assert.Equal(new[] { 'a' }, sink.Received);
    }

    [Fact]
    public void OfferChar_ShadowScopeSink_NeverConsulted()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var shadow = new RecordingScope("shadow", journal, isLive: false);
        var sink = new RecordingCharSink(_ => true);
        shadow.CharSinkOverride = sink;
        core.Push(shadow);

        Assert.False(core.OfferChar('a'));
        Assert.Empty(sink.Received);
    }

    [Fact]
    public void OfferChar_LiveModalWithoutSink_BlocksLowerSink()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var lowerSink = new RecordingCharSink(_ => true);
        lower.CharSinkOverride = lowerSink;
        var topModal = new RecordingScope("topModal", journal); // modal, no sink
        core.Push(lower);
        core.Push(topModal);

        Assert.False(core.OfferChar('a'));
        Assert.Empty(lowerSink.Received);
    }

    [Fact]
    public void OfferChar_LiveNonModalWithoutSink_LetsCharReachLowerSink()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        var lowerSink = new RecordingCharSink(_ => true);
        lower.CharSinkOverride = lowerSink;
        var topNonModal = new RecordingScope("topNonModal", journal, isModal: false); // no sink
        core.Push(lower);
        core.Push(topNonModal);

        Assert.True(core.OfferChar('a'));
        Assert.Equal(new[] { 'a' }, lowerSink.Received);
    }

    [Fact]
    public void Dispatch_EmptyStackOrNullCatalog_ReturnsFalse()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var snapshot = new KeyEventSnapshot(KeyCode.M);

        Assert.False(core.Dispatch(snapshot, catalog, out string id1));
        Assert.Null(id1);

        core.Push(new RecordingScope("a", new List<string>()));
        Assert.False(core.Dispatch(snapshot, null, out string id2));
        Assert.Null(id2);
    }

    [Fact]
    public void ClaimedBelowModals_SeesClaimsBelowTheLowestModal_IgnoringWhenGuards()
    {
        var core = new FocusStackCore();
        var catalog = new ActionCatalog();
        var journal = new List<string>();
        var map = new RecordingScope("map", journal, isModal: false);
        var observer = new RecordingScope("observer", journal, isModal: false);
        var menu = new RecordingScope("menu", journal);
        core.Push(map);
        core.Push(observer);
        core.Push(menu);
        catalog.Register(Screen("map", "map.search", "Z"));
        catalog.Register(Screen("observer", "observer.peek", "X"));
        catalog.Register(Screen("menu", "menu.only", "Q"));
        map.AddClaim("map.search", when: () => false);
        observer.AddClaim("observer.peek", propagate: true);
        menu.AddClaim("menu.only");

        Assert.True(core.ClaimedBelowModals(KeyChord.Parse("Z"), catalog));
        Assert.False(core.ClaimedBelowModals(KeyChord.Parse("Ctrl+Z"), catalog));
        Assert.False(core.ClaimedBelowModals(KeyChord.Parse("X"), catalog));
        Assert.False(core.ClaimedBelowModals(KeyChord.Parse("Q"), catalog));
    }
}
