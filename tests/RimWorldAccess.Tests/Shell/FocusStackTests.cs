using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Test double shared with FocusDispatchTests.cs: a bare FocusScope that
/// appends lifecycle events to an injected, shared journal (so tests can
/// assert cross-scope ordering) plus thin public wrappers over the protected
/// Claim/PassThrough surface used by the dispatch tests.
/// </summary>
internal sealed class RecordingScope : FocusScope
{
    private readonly string name;
    private readonly List<string> journal;
    private readonly bool isModal;
    private readonly bool isLive;
    private readonly bool? ownsGameInput;
    private readonly bool trackAfterFocusDispatch;

    public RecordingScope(string name, List<string> journal, bool isModal = true, bool isLive = true, bool? ownsGameInput = null, bool trackAfterFocusDispatch = false)
    {
        this.name = name;
        this.journal = journal;
        this.isModal = isModal;
        this.isLive = isLive;
        this.ownsGameInput = ownsGameInput;
        this.trackAfterFocusDispatch = trackAfterFocusDispatch;
    }

    public override string Name => name;
    public override bool IsModal => isModal;
    public override bool IsLive => isLive;
    public override bool OwnsGameInput => ownsGameInput ?? base.OwnsGameInput;

    public ICharSink CharSinkOverride { get; set; }
    public override ICharSink CharSink => CharSinkOverride;

    public override void OnPush() => journal.Add(name + ":push");
    public override void OnPop() => journal.Add(name + ":pop");
    public override void OnFocus() => journal.Add(name + ":focus");
    public override void OnUnfocus() => journal.Add(name + ":unfocus");

    // Opt-in only (default false): dozens of existing tests assert EXACT journal
    // arrays against OnPush/OnPop/OnFocus/OnUnfocus alone, so this must stay
    // silent unless a test explicitly asks to observe the new S2 hook.
    public override void AfterFocusDispatch()
    {
        if (trackAfterFocusDispatch)
        {
            journal.Add(name + ":afterFocusDispatch");
        }
    }

    public void AddClaim(string id, Action<KeyEventSnapshot> handler = null, bool propagate = false, Func<bool> when = null)
    {
        Claim(id, e => handler?.Invoke(e), propagate, when);
    }

    public void AddPass(string id) => PassThrough(id);
}

public class FocusStackTests
{
    [Fact]
    public void Push_Pop_Basics()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);

        core.Push(a);
        Assert.Same(a, core.Top);
        Assert.Equal(1, core.Count);

        Assert.True(core.Pop(a));
        Assert.Null(core.Top);
        Assert.Equal(0, core.Count);

        Assert.False(core.Pop(a)); // no longer on the stack
        Assert.False(core.Pop(null));
    }

    [Fact]
    public void Push_Null_Throws()
    {
        var core = new FocusStackCore();
        Assert.Throws<System.ArgumentNullException>(() => core.Push(null));
    }

    [Fact]
    public void Push_LifecycleOrder_UnfocusesOldTop_PushesAndFocusesNew()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        journal.Clear();

        core.Push(b);

        Assert.Equal(new[] { "a:unfocus", "b:push", "b:focus" }, journal);
    }

    [Fact]
    public void Pop_Top_LifecycleOrder_UnfocusPopThenFocusBelow()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        Assert.True(core.Pop(b));

        Assert.Equal(new[] { "b:unfocus", "b:pop", "a:focus" }, journal);
        Assert.Same(a, core.Top);
    }

    [Fact]
    public void Pop_MidStack_FiresOnlyPop_TopUnaffected()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        var c = new RecordingScope("c", journal);
        core.Push(a);
        core.Push(b);
        core.Push(c);
        journal.Clear();

        Assert.True(core.Pop(b));

        Assert.Equal(new[] { "b:pop" }, journal);
        Assert.Same(c, core.Top);
        Assert.Equal(2, core.Count);
    }

    [Fact]
    public void Push_CurrentTopAgain_IsNoop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        core.Push(a);
        journal.Clear();

        core.Push(a);

        Assert.Empty(journal);
        Assert.Same(a, core.Top);
        Assert.Equal(1, core.Count);
    }

    [Fact]
    public void Push_ExistingLowerScope_RefloatsToTop_NoDuplicatePush()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.Push(a); // a is at the bottom; this re-floats it to the top

        Assert.Equal(new[] { "b:unfocus", "a:focus" }, journal);
        Assert.Same(a, core.Top);
        Assert.Equal(2, core.Count);
    }

    [Fact]
    public void InsertBelow_PutsScopeBeneathReference_TopKeepsFocus()
    {
        // Models the nested-Add inversion: an inner window's scope (meme) is
        // already on top when the outer window's scope (hub) attaches. The hub
        // must land BELOW the meme scope, and the meme scope must keep focus.
        var core = new FocusStackCore();
        var journal = new List<string>();
        var meme = new RecordingScope("meme", journal);
        var hub = new RecordingScope("hub", journal);

        core.Push(meme);
        journal.Clear();

        core.InsertBelow(hub, meme);

        // hub gets first-time setup but NOT focus; meme is untouched and stays top.
        Assert.Equal(new[] { "hub:push" }, journal);
        Assert.Same(meme, core.Top);
        Assert.Equal(2, core.Count);
        Assert.Same(hub, core.ScopesBottomUp[0]);
        Assert.Same(meme, core.ScopesBottomUp[1]);

        // Popping the meme picker hands focus down to the hub, as after a real
        // meme-picker close.
        journal.Clear();
        core.Pop(meme);
        Assert.Equal(new[] { "meme:unfocus", "meme:pop", "hub:focus" }, journal);
        Assert.Same(hub, core.Top);
    }

    [Fact]
    public void InsertBelow_ReferenceNotOnStack_FallsBackToPush()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var orphan = new RecordingScope("orphan", journal);

        core.InsertBelow(a, orphan); // orphan never pushed -> fall back to a top push

        Assert.Same(a, core.Top);
        Assert.Equal(1, core.Count);
    }

    [Fact]
    public void InsertBelow_AlreadyPresent_IsNoop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.InsertBelow(a, b); // a already on the stack

        Assert.Empty(journal);
        Assert.Equal(2, core.Count);
        Assert.Same(b, core.Top);
    }

    [Fact]
    public void OwnsGameInput_DefaultsToIsModal()
    {
        var journal = new List<string>();
        Assert.True(new RecordingScope("modal", journal, isModal: true).OwnsGameInput);
        Assert.False(new RecordingScope("nonModal", journal, isModal: false).OwnsGameInput);
    }

    [Fact]
    public void AnyLiveInputOwner_TracksModalAndOverriddenOwners()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();

        Assert.False(core.AnyLiveInputOwner);

        // A live non-modal scope without the override does not own input.
        var ambient = new RecordingScope("ambient", journal, isModal: false, isLive: true);
        core.Push(ambient);
        Assert.False(core.AnyLiveInputOwner);
        Assert.False(core.AnyLiveModal);

        // A non-modal owner (the placement/viewing/gizmo shape) owns input
        // without tripping AnyLiveModal - the P4-7 distinction.
        var placement = new RecordingScope("placement", journal, isModal: false, isLive: true, ownsGameInput: true);
        core.Push(placement);
        Assert.True(core.AnyLiveInputOwner);
        Assert.False(core.AnyLiveModal);

        core.Pop(placement);
        Assert.False(core.AnyLiveInputOwner);

        // A live modal scope owns input via the default.
        var menu = new RecordingScope("menu", journal, isModal: true, isLive: true);
        core.Push(menu);
        Assert.True(core.AnyLiveInputOwner);
        Assert.True(core.AnyLiveModal);

        core.Pop(menu);
        Assert.False(core.AnyLiveInputOwner);
    }

    [Fact]
    public void AnyLiveInputOwner_IgnoresShadowScopes()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();

        // A shadow (IsLive=false) scope never owns input, even modal or
        // explicitly flagged - mirrors AnyLiveModal's live gate.
        core.Push(new RecordingScope("shadowModal", journal, isModal: true, isLive: false));
        core.Push(new RecordingScope("shadowOwner", journal, isModal: false, isLive: false, ownsGameInput: true));
        Assert.False(core.AnyLiveInputOwner);
    }

    [Fact]
    public void Contains_ReflectsPresence_RegardlessOfPosition()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);

        Assert.False(core.Contains(a));
        Assert.False(core.Contains(null));

        core.Push(a);
        core.Push(b);

        Assert.True(core.Contains(a)); // present but buried
        Assert.True(core.Contains(b)); // present and top

        core.Pop(b);
        Assert.False(core.Contains(b));
        Assert.True(core.Contains(a));
    }

    [Fact]
    public void PushIfAbsent_OnBuriedScope_DoesNotRefocus()
    {
        // Regression: a per-frame reconciler must be able to keep a scope PRESENT
        // without re-floating it to the top. Re-floating a buried scope fires
        // OnFocus every frame, which for the ideo overlay scopes re-announces the
        // focused precept - the bug that flooded the screen reader while a
        // sub-picker float menu sat above the overlay. Guarding the Push with
        // Contains is what the mirror relies on to avoid the flood.
        var core = new FocusStackCore();
        var journal = new List<string>();
        var overlay = new RecordingScope("overlay", journal);
        var floatMenu = new RecordingScope("floatMenu", journal);

        core.Push(overlay);
        core.Push(floatMenu); // legitimately above the overlay
        journal.Clear();

        // Simulate several reconcile frames: overlay stays present, so no Push.
        for (int frame = 0; frame < 5; frame++)
        {
            if (!core.Contains(overlay))
            {
                core.Push(overlay);
            }
        }

        Assert.Empty(journal); // no focus/unfocus churn on the buried overlay
        Assert.Same(floatMenu, core.Top);

        // When the float menu pops, the overlay regains focus exactly once.
        core.Pop(floatMenu);
        Assert.Equal(new List<string> { "floatMenu:unfocus", "floatMenu:pop", "overlay:focus" }, journal);
    }

    [Fact]
    public void Pop_OnCurrentBase_AlsoVacatesBase()
    {
        // Pop() has no explicit "is this the base?" contract, but it clears the
        // base pointer as a side effect whenever the popped scope happens to be
        // it - so a caller that pops the base directly (bypassing SetBase(null))
        // silently converts the stack to a no-base state.
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);
        core.SetBase(baseScope);

        Assert.True(core.Pop(baseScope));

        Assert.Null(core.Base);
        Assert.Equal(0, core.Count);
    }

    [Fact]
    public void SetBase_IntoEmptyStack_PushesAndFocuses()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);

        core.SetBase(baseScope);

        Assert.Equal(new[] { "base:push", "base:focus" }, journal);
        Assert.Same(baseScope, core.Base);
        Assert.Same(baseScope, core.Top);
    }

    [Fact]
    public void SetBase_SwapWithScopesAbove_NoFocusChurn_CountUnchanged()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var oldBase = new RecordingScope("oldBase", journal, isModal: false);
        var overlay = new RecordingScope("overlay", journal);
        core.SetBase(oldBase);
        core.Push(overlay);
        journal.Clear();
        int countBefore = core.Count;

        var newBase = new RecordingScope("newBase", journal, isModal: false);
        core.SetBase(newBase);

        Assert.Equal(new[] { "oldBase:pop", "newBase:push" }, journal);
        Assert.Same(overlay, core.Top); // unchanged; no unfocus/focus fired on it
        Assert.Same(newBase, core.Base);
        Assert.Equal(countBefore, core.Count);
    }

    [Fact]
    public void SetBase_SwapWhenBaseIsOnlyScope_FullLifecycle()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var oldBase = new RecordingScope("oldBase", journal, isModal: false);
        core.SetBase(oldBase);
        journal.Clear();

        var newBase = new RecordingScope("newBase", journal, isModal: false);
        core.SetBase(newBase);

        Assert.Equal(new[] { "oldBase:unfocus", "oldBase:pop", "newBase:push", "newBase:focus" }, journal);
        Assert.Same(newBase, core.Base);
        Assert.Same(newBase, core.Top);
    }

    [Fact]
    public void SetBase_SameInstance_IsNoop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);
        core.SetBase(baseScope);
        journal.Clear();

        core.SetBase(baseScope);

        Assert.Empty(journal);
    }

    [Fact]
    public void SetBase_Null_VacatesBase()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);
        core.SetBase(baseScope);
        journal.Clear();

        core.SetBase(null);

        Assert.Equal(new[] { "base:unfocus", "base:pop" }, journal);
        Assert.Null(core.Base);
        Assert.Equal(0, core.Count);
    }

    [Fact]
    public void SetBase_ScopeAlreadyOnStack_Throws()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var overlay = new RecordingScope("overlay", journal);
        core.Push(overlay);

        Assert.Throws<System.InvalidOperationException>(() => core.SetBase(overlay));
    }

    [Fact]
    public void ClearToBase_PopsAboveBase_ThenFocusesBaseOnce_RepeatIsNoop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.SetBase(baseScope);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.ClearToBase(GameBoundary.GameStart);

        Assert.Equal(new[] { "b:unfocus", "b:pop", "a:unfocus", "a:pop", "base:focus" }, journal);
        Assert.Same(baseScope, core.Top);
        Assert.Equal(1, core.Count);

        journal.Clear();
        core.ClearToBase(GameBoundary.GameStart);

        Assert.Empty(journal);
    }

    [Fact]
    public void ClearToBase_StaleBase_PopsWithoutFocusingIt()
    {
        // At the GameStart boundary the base is still the previous scene's
        // scope (main menu during a save load); a non-live base must not
        // announce itself.
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false, isLive: false);
        var a = new RecordingScope("a", journal);
        core.SetBase(baseScope);
        core.Push(a);
        journal.Clear();

        core.ClearToBase(GameBoundary.GameStart);

        Assert.Equal(new[] { "a:unfocus", "a:pop" }, journal);
        Assert.Same(baseScope, core.Top);
    }

    [Fact]
    public void ClearToBase_NoBase_ClearsWholeStack()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.ClearToBase(GameBoundary.MainMenu);

        Assert.Equal(new[] { "b:unfocus", "b:pop", "a:unfocus", "a:pop" }, journal);
        Assert.Equal(0, core.Count);
        Assert.Null(core.Base);
    }

    [Fact]
    public void ClearAll_EmptiesEverythingIncludingBase()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false);
        var a = new RecordingScope("a", journal);
        core.SetBase(baseScope);
        core.Push(a);

        core.ClearAll();

        Assert.Equal(0, core.Count);
        Assert.Null(core.Base);
        Assert.Null(core.Top);
    }

    [Fact]
    public void AnyLiveModal_FalseForLiveNonModalAndShadowModal()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var liveNonModal = new RecordingScope("liveNonModal", journal, isModal: false, isLive: true);
        core.Push(liveNonModal);
        Assert.False(core.AnyLiveModal);

        var shadowModal = new RecordingScope("shadowModal", journal, isModal: true, isLive: false);
        core.Push(shadowModal);
        Assert.False(core.AnyLiveModal);
    }

    [Fact]
    public void AnyLiveModal_TrueWhenLiveModalAnywhereOnStack_FalseAfterPop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var liveModal = new RecordingScope("liveModal", journal, isModal: true, isLive: true);
        var liveNonModalOnTop = new RecordingScope("liveNonModalOnTop", journal, isModal: false, isLive: true);

        core.Push(liveModal);
        Assert.True(core.AnyLiveModal);

        core.Push(liveNonModalOnTop); // buries liveModal; it's no longer on top
        Assert.True(core.AnyLiveModal); // still true: the check scans the whole stack

        core.Pop(liveModal);
        Assert.False(core.AnyLiveModal);
    }

    [Fact]
    public void RefocusTop_LiveTop_ReceivesOnFocus_StackUntouched()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var below = new RecordingScope("below", journal);
        var top = new RecordingScope("top", journal, isModal: true, isLive: true);
        core.Push(below);
        core.Push(top);
        journal.Clear();

        core.RefocusTop();

        Assert.Equal(new[] { "top:focus" }, journal);
        Assert.Same(top, core.Top);
        Assert.Equal(2, core.Count);
    }

    [Fact]
    public void RefocusTop_ShadowTopOrEmptyStack_DoesNothing()
    {
        var core = new FocusStackCore();
        core.RefocusTop(); // empty stack: no throw

        var journal = new List<string>();
        var shadow = new RecordingScope("shadow", journal, isModal: true, isLive: false);
        core.Push(shadow);
        journal.Clear();

        core.RefocusTop();

        Assert.Empty(journal);
    }

    [Fact]
    public void Push_FiresAfterFocusDispatch_ImmediatelyAfterOnFocus()
    {
        // AfterFocusDispatch must fire, and fire AFTER
        // OnFocus, at every one of FocusStackCore's five OnFocus call sites -
        // this is what lets ScreenScope speak the freshly focused item without
        // ever preempting a subclass's own synchronous title announcement
        // (which runs inside the OnFocus dispatch itself, before this fires).
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal, trackAfterFocusDispatch: true);

        core.Push(a);

        Assert.Equal(new[] { "a:push", "a:focus", "a:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void Pop_RefocusingBelow_FiresAfterFocusDispatchOnNewTop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal, trackAfterFocusDispatch: true);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.Pop(b);

        Assert.Equal(new[] { "b:unfocus", "b:pop", "a:focus", "a:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void SetBase_IntoEmptyStack_FiresAfterFocusDispatch()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false, trackAfterFocusDispatch: true);

        core.SetBase(baseScope);

        Assert.Equal(new[] { "base:push", "base:focus", "base:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void SetBase_SwapWithScopesAbove_NeverFiresAfterFocusDispatch_TopUnchanged()
    {
        // The new base does not become the stack's focused top when scopes already
        // sit above it, so it must not receive OnFocus/AfterFocusDispatch either.
        var core = new FocusStackCore();
        var journal = new List<string>();
        var oldBase = new RecordingScope("oldBase", journal, isModal: false);
        var overlay = new RecordingScope("overlay", journal);
        core.SetBase(oldBase);
        core.Push(overlay);
        journal.Clear();

        var newBase = new RecordingScope("newBase", journal, isModal: false, trackAfterFocusDispatch: true);
        core.SetBase(newBase);

        Assert.DoesNotContain("newBase:afterFocusDispatch", journal);
    }

    [Fact]
    public void ClearToBase_FiresAfterFocusDispatchOnBase()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("base", journal, isModal: false, trackAfterFocusDispatch: true);
        var a = new RecordingScope("a", journal);
        core.SetBase(baseScope);
        core.Push(a);
        journal.Clear();

        core.ClearToBase(GameBoundary.GameStart);

        Assert.Equal(new[] { "a:unfocus", "a:pop", "base:focus", "base:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void RefocusTop_FiresAfterFocusDispatch()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var below = new RecordingScope("below", journal);
        var top = new RecordingScope("top", journal, isModal: true, isLive: true, trackAfterFocusDispatch: true);
        core.Push(below);
        core.Push(top);
        journal.Clear();

        core.RefocusTop();

        Assert.Equal(new[] { "top:focus", "top:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void DebugDump_EmptyStack_ReportsEmpty()
    {
        var core = new FocusStackCore();
        Assert.Contains("(empty)", core.DebugDump());
    }

    [Fact]
    public void DebugDump_WithScopes_ShowsNamesBaseMarkerAndLiveness()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var baseScope = new RecordingScope("mapBase", journal, isModal: false, isLive: true);
        var liveOverlay = new RecordingScope("dialog", journal, isModal: true, isLive: true);
        var shadowOverlay = new RecordingScope("legacyShadow", journal, isModal: true, isLive: false);
        core.SetBase(baseScope);
        core.Push(liveOverlay);
        core.Push(shadowOverlay);

        string dump = core.DebugDump();
        var lines = dump.Split('\n');

        var baseLine = Assert.Single(lines, l => l.Contains("mapBase"));
        Assert.Contains("base,", baseLine);
        Assert.Contains("live", baseLine);

        var dialogLine = Assert.Single(lines, l => l.Contains("dialog"));
        Assert.Contains("live", dialogLine);
        Assert.DoesNotContain("base,", dialogLine);

        var shadowLine = Assert.Single(lines, l => l.Contains("legacyShadow"));
        Assert.Contains("shadow", shadowLine);
    }

    [Fact]
    public void Reconcile_ReFloatChurnInsideBracket_FiresNoFocusEvents()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal, trackAfterFocusDispatch: true);
        var b = new RecordingScope("b", journal, trackAfterFocusDispatch: true);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.Push(a); // re-float
        core.Push(b); // re-float back
        core.EndReconcile();

        Assert.Empty(journal); // net top unchanged; no focus/unfocus/afterFocusDispatch churn
        Assert.Same(b, core.Top);
        Assert.Same(a, core.ScopesBottomUp[0]);
        Assert.Same(b, core.ScopesBottomUp[1]);
    }

    [Fact]
    public void Reconcile_RefloatTrace_OnlyWhenTheBatchChangesTheTop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("trace-a", journal);
        var b = new RecordingScope("trace-b", journal);
        core.Push(a);
        core.Push(b);

        // The sink is static and other test classes run in parallel, so keep only this test's lines.
        var trace = new List<string>();
        Action<string> previous = FocusStackCore.ScopeTraceSink;
        FocusStackCore.ScopeTraceSink = line =>
        {
            if (line.Contains("trace-"))
            {
                lock (trace) trace.Add(line);
            }
        };
        try
        {
            core.BeginReconcile();
            core.Push(a);
            core.Push(b);
            core.EndReconcile();
            Assert.Empty(trace);

            core.BeginReconcile();
            core.Push(a);
            core.EndReconcile();
            Assert.Equal(new[] { "refloat trace-a" }, trace);
        }
        finally
        {
            FocusStackCore.ScopeTraceSink = previous;
        }
    }

    [Fact]
    public void Reconcile_NewTopInsideBracket_FiresFocusEventsOnceAtEnd()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal, trackAfterFocusDispatch: true);
        var b = new RecordingScope("b", journal, trackAfterFocusDispatch: true);
        var c = new RecordingScope("c", journal, trackAfterFocusDispatch: true);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.Push(c);
        Assert.Equal(new[] { "c:push" }, journal); // nothing fires before EndReconcile
        core.EndReconcile();

        Assert.Equal(new[] { "c:push", "b:unfocus", "c:focus", "c:afterFocusDispatch" }, journal);
        Assert.Same(c, core.Top);
    }

    [Fact]
    public void Reconcile_PopOfTopInsideBracket_SkipsUnfocusOnPoppedScope()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal, trackAfterFocusDispatch: true);
        var b = new RecordingScope("b", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.Pop(b);
        Assert.Equal(new[] { "b:pop" }, journal); // popped off the stack: no OnUnfocus, not "merely buried"
        core.EndReconcile();

        Assert.Equal(new[] { "b:pop", "a:focus", "a:afterFocusDispatch" }, journal);
        Assert.Same(a, core.Top);
    }

    [Fact]
    public void Reconcile_BuriedPreTopStillOnStack_GetsExactlyOneUnfocusAtEnd()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        var c = new RecordingScope("c", journal);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.Push(c);
        core.EndReconcile();

        Assert.Single(journal, entry => entry == "b:unfocus");
        Assert.True(core.Contains(b)); // buried, not removed
    }

    [Fact]
    public void Reconcile_NestedBrackets_OnlyOutermostEndFlushes()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal);
        var c = new RecordingScope("c", journal, trackAfterFocusDispatch: true);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.BeginReconcile();
        core.Push(c);
        core.EndReconcile(); // inner: depth still > 0, must not flush
        Assert.Equal(new[] { "c:push" }, journal);
        core.EndReconcile(); // outer: flushes

        Assert.Equal(new[] { "c:push", "b:unfocus", "c:focus", "c:afterFocusDispatch" }, journal);
    }

    [Fact]
    public void Reconcile_NetTopUnchanged_PopAndRepushSameScope_FiresNoFocusEvents()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        var b = new RecordingScope("b", journal, trackAfterFocusDispatch: true);
        core.Push(a);
        core.Push(b);
        journal.Clear();

        core.BeginReconcile();
        core.Pop(b);
        core.Push(b);
        core.EndReconcile();

        // Structural events (pop/push) still fire; focus/unfocus/afterFocusDispatch do not,
        // since the effective top (b) never actually changed across the bracket.
        Assert.Equal(new[] { "b:pop", "b:push" }, journal);
        Assert.Same(b, core.Top);
    }

    [Fact]
    public void EndReconcile_WithoutBegin_IsSafeNoop()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var a = new RecordingScope("a", journal);
        core.Push(a);
        journal.Clear();

        core.EndReconcile(); // unbalanced call; must not throw or fire anything

        Assert.Empty(journal);
        Assert.Same(a, core.Top);
    }
}
