using System;
using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The game boundary that triggered a stack reset. FinalizeInit covers new
    /// games AND saves loaded from anywhere (the canonical in-game reset
    /// point); MainMenu covers returning to the entry scene.
    /// </summary>
    public enum GameBoundary
    {
        GameStart,
        MainMenu,
    }

    /// <summary>
    /// Ordered stack of <see cref="FocusScope"/>s: one optional ambient base at the bottom (map,
    /// world or main menu, swapped in place as game state changes) with window and overlay scopes
    /// above it. Dispatch walks top-down, the first consuming claim wins, and a live modal scope
    /// masks everything beneath it. An instance core so tests can build isolated stacks; the game
    /// talks to the static <see cref="FocusStack"/> front. PURE: links into the test project.
    /// </summary>
    public sealed class FocusStackCore
    {
        private readonly List<FocusScope> scopes = new List<FocusScope>();
        private FocusScope baseScope;
        private int reconcileDepth;
        private FocusScope reconcilePreTop;
        private FocusScope reconcileRefloated;
        private int refocusSuppressionDepth;

        /// <summary>True inside a <see cref="BeginReconcile"/>/<see cref="EndReconcile"/> bracket.</summary>
        private bool Reconciling
        {
            get { return reconcileDepth > 0; }
        }

        /// <summary>
        /// Optional sink for scope push/pop transitions, one pre-formatted line each. Wired by the
        /// DEBUG-only QA recorder so this PURE class references no game-coupled type; null (the
        /// default) makes every push/pop a single no-op check.
        /// </summary>
        public static Action<string> ScopeTraceSink;

        /// <summary>
        /// Optional sink for a claim handler that threw, taking the action id and the exception;
        /// null (the default) makes a throwing handler silent but still contained
        /// (see <see cref="Dispatch"/>).
        /// </summary>
        public static Action<string, Exception> HandlerErrorSink;

        /// <summary>The focused scope (top of the stack), or null when empty.</summary>
        public FocusScope Top
        {
            get { return scopes.Count == 0 ? null : scopes[scopes.Count - 1]; }
        }

        /// <summary>The ambient base scope occupying the bottom slot, or null.</summary>
        public FocusScope Base
        {
            get { return baseScope; }
        }

        public int Count
        {
            get { return scopes.Count; }
        }

        /// <summary>
        /// Whether <paramref name="scope"/> is anywhere on the stack. Lets a per-frame reconciler
        /// keep a scope PRESENT without re-floating it, which would fire
        /// <see cref="FocusScope.OnFocus"/> every frame.
        /// </summary>
        public bool Contains(FocusScope scope)
        {
            return scope != null && scopes.Contains(scope);
        }

        /// <summary>True while any live modal scope is on the stack.</summary>
        public bool AnyLiveModal
        {
            get
            {
                for (int i = 0; i < scopes.Count; i++)
                {
                    if (scopes[i].IsLive && scopes[i].IsModal)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// True while any live scope owns the game's keyboard
        /// (<see cref="FocusScope.OwnsGameInput"/>: modal scopes by default, plus the non-modal
        /// input owners that override it).
        /// </summary>
        public bool AnyLiveInputOwner
        {
            get
            {
                for (int i = 0; i < scopes.Count; i++)
                {
                    if (scopes[i].IsLive && scopes[i].OwnsGameInput)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Bottom-up view for diagnostics; do not mutate.</summary>
        public IReadOnlyList<FocusScope> ScopesBottomUp
        {
            get { return scopes; }
        }

        /// <summary>
        /// Adds a scope above everything on the stack. Pushing the current top again is a no-op;
        /// pushing one already lower re-floats it to the top — defensive self-healing, since
        /// reaching that path is an ordering bug but must never cost the user a dead keyboard.
        /// Inside a <see cref="BeginReconcile"/> bracket a re-float reorders the stack but fires no
        /// focus events; <see cref="EndReconcile"/> fires them once, and only if the effective top
        /// changed, which is what stops per-frame mirror reconciles from re-announcing every frame.
        /// A re-float traces as "refloat", not "push", and inside a reconcile bracket it traces
        /// only from <see cref="EndReconcile"/>, when the batch changed the effective top: mirrors
        /// re-float every pass, and tracing each one buries genuine transitions in the recorder.
        /// </summary>
        public void Push(FocusScope scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }
            FocusScope oldTop = Top;
            if (ReferenceEquals(oldTop, scope))
            {
                return;
            }
            bool refloat = scopes.Remove(scope);
            if (oldTop != null && !Reconciling)
            {
                oldTop.OnUnfocus();
            }
            scopes.Add(scope);
            if (!refloat)
            {
                scope.OnPush();
            }
            if (!Reconciling)
            {
                scope.OnFocus();
                scope.AfterFocusDispatch();
            }
            if (refloat && Reconciling)
            {
                reconcileRefloated = scope;
            }
            else
            {
                ScopeTraceSink?.Invoke((refloat ? "refloat " : "push ") + scope.Name);
            }
        }

        /// <summary>
        /// Opens a batching bracket: Push/Pop/SetBase keep reordering the stack and firing
        /// structural events, but <see cref="FocusScope.OnFocus"/>/<see cref="FocusScope.OnUnfocus"/>/
        /// <see cref="FocusScope.AfterFocusDispatch"/> are held until the matching
        /// <see cref="EndReconcile"/> fires them once for the net top-of-stack change. Nestable;
        /// only the outermost EndReconcile flushes. Exists for the dispatcher's per-frame mirror
        /// walk, where every mirror re-floats its scope unconditionally.
        /// </summary>
        public void BeginReconcile()
        {
            if (reconcileDepth == 0)
            {
                reconcilePreTop = Top;
            }
            reconcileDepth++;
        }

        /// <summary>Closes a <see cref="BeginReconcile"/> bracket; see its doc comment.</summary>
        public void EndReconcile()
        {
            if (reconcileDepth == 0)
            {
                return;
            }
            reconcileDepth--;
            if (reconcileDepth > 0)
            {
                return;
            }
            FocusScope preTop = reconcilePreTop;
            FocusScope refloated = reconcileRefloated;
            reconcilePreTop = null;
            reconcileRefloated = null;
            FocusScope newTop = Top;
            if (ReferenceEquals(newTop, preTop))
            {
                return;
            }
            if (newTop != null && ReferenceEquals(newTop, refloated))
            {
                ScopeTraceSink?.Invoke("refloat " + newTop.Name);
            }
            // A scope popped mid-batch already ran its teardowns, so OnUnfocus fires only when the
            // pre-batch top is still on the stack, merely buried.
            if (preTop != null && scopes.Contains(preTop))
            {
                preTop.OnUnfocus();
            }
            if (newTop != null)
            {
                newTop.OnFocus();
                newTop.AfterFocusDispatch();
            }
        }

        /// <summary>
        /// Inserts <paramref name="scope"/> immediately below <paramref name="reference"/>, leaving
        /// the current top and its focus untouched. Keeps window-attached scopes matching the
        /// WindowStack z-order in the one case <see cref="Push"/> gets wrong: an outer window whose
        /// PostOpen synchronously opens and attaches a scope for an inner window before the outer
        /// window's own Add postfix runs. The inserted scope is not top, so it gets
        /// <see cref="FocusScope.OnPush"/> but not <see cref="FocusScope.OnFocus"/>. Falls back to
        /// <see cref="Push"/> when the reference is absent, so the keyboard is never left dead.
        /// </summary>
        public void InsertBelow(FocusScope scope, FocusScope reference)
        {
            if (scope == null)
            {
                throw new ArgumentNullException("scope");
            }
            if (reference == null)
            {
                throw new ArgumentNullException("reference");
            }
            if (scopes.Contains(scope))
            {
                return;
            }
            int refIndex = scopes.IndexOf(reference);
            if (refIndex < 0)
            {
                Push(scope);
                return;
            }
            // scopes is bottom-up, so inserting AT the reference's index shifts it up by one and
            // the new scope lands directly beneath.
            scopes.Insert(refIndex, scope);
            scope.OnPush();
        }

        /// <summary>
        /// Removes a scope wherever it sits, returning false when it was not on the stack (common
        /// when close paths race). Popping the top hands focus back to the scope below.
        /// </summary>
        public bool Pop(FocusScope scope)
        {
            if (scope == null)
            {
                return false;
            }
            int index = scopes.IndexOf(scope);
            if (index < 0)
            {
                return false;
            }
            bool wasTop = index == scopes.Count - 1;
            if (wasTop && !Reconciling)
            {
                scope.OnUnfocus();
            }
            scopes.RemoveAt(index);
            if (ReferenceEquals(baseScope, scope))
            {
                baseScope = null;
            }
            DispatchPop(scope);
            ScopeTraceSink?.Invoke("pop " + scope.Name);
            if (wasTop && !Reconciling)
            {
                FocusScope newTop = Top;
                if (newTop != null)
                {
                    newTop.OnFocus();
                    newTop.AfterFocusDispatch();
                }
            }
            return true;
        }

        /// <summary>
        /// Installs or swaps the ambient base scope without disturbing scopes above it: a map/world
        /// toggle mid-game must not tear down open overlays. Passing the current base is a no-op,
        /// null vacates the slot, and a scope already elsewhere on the stack cannot become the base.
        /// </summary>
        public void SetBase(FocusScope scope)
        {
            if (ReferenceEquals(baseScope, scope))
            {
                return;
            }
            if (scope != null && scopes.Contains(scope))
            {
                throw new InvalidOperationException(
                    "Scope '" + scope.Name + "' is already on the stack and cannot become the base.");
            }
            FocusScope old = baseScope;
            if (old != null)
            {
                bool wasTop = ReferenceEquals(Top, old);
                if (wasTop && !Reconciling)
                {
                    old.OnUnfocus();
                }
                scopes.Remove(old);
                baseScope = null;
                DispatchPop(old);
                ScopeTraceSink?.Invoke("pop " + old.Name);
            }
            if (scope != null)
            {
                scopes.Insert(0, scope);
                baseScope = scope;
                scope.OnPush();
                if (ReferenceEquals(Top, scope) && !Reconciling)
                {
                    scope.OnFocus();
                    scope.AfterFocusDispatch();
                }
            }
        }

        /// <summary>
        /// Pops every scope above the base, top-down with the full lifecycle. Runs at both game
        /// boundaries so no scope, and therefore no screen state, survives into a new session.
        /// </summary>
        public void ClearToBase(GameBoundary why)
        {
            bool removedAny = false;
            while (scopes.Count > 0)
            {
                FocusScope top = Top;
                if (ReferenceEquals(top, baseScope))
                {
                    break;
                }
                top.OnUnfocus();
                scopes.RemoveAt(scopes.Count - 1);
                DispatchPop(top);
                ScopeTraceSink?.Invoke("pop " + top.Name);
                removedAny = true;
            }
            // A stale base must stay silent: at the GameStart boundary it is still the previous
            // scene's scope until AmbientScopeSelector swaps it, so announcing here speaks the main
            // menu mid-load.
            if (removedAny && baseScope != null && baseScope.IsLive)
            {
                baseScope.OnFocus();
                baseScope.AfterFocusDispatch();
            }
        }

        /// <summary>
        /// Re-fires <see cref="FocusScope.OnFocus"/> on the current live top without changing the
        /// stack. Windowless overlays own the keyboard without touching this stack, so their close
        /// is invisible here and each close path calls this instead. Shadow tops are skipped.
        /// </summary>
        public void RefocusTop()
        {
            if (refocusSuppressionDepth > 0)
                return;
            FocusScope top = Top;
            if (top != null && top.IsLive)
            {
                top.OnFocus();
                top.AfterFocusDispatch();
            }
        }

        /// <summary>
        /// Suppresses <see cref="RefocusTop"/> for the returned scope's lifetime. A screen's close
        /// path speaks its closure and then closes the window it drove; that removal is a
        /// consequence of the close, not a return to a surface, and re-announcing would talk over
        /// the closure line.
        /// </summary>
        public IDisposable SuppressRefocus()
        {
            refocusSuppressionDepth++;
            return new RefocusSuppression(this);
        }

        private sealed class RefocusSuppression : IDisposable
        {
            private readonly FocusStackCore owner;
            private bool disposed;

            internal RefocusSuppression(FocusStackCore owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                owner.refocusSuppressionDepth--;
            }
        }

        /// <summary>Remove everything including the base (tests, full teardown).</summary>
        public void ClearAll()
        {
            while (scopes.Count > 0)
            {
                FocusScope top = Top;
                top.OnUnfocus();
                scopes.RemoveAt(scopes.Count - 1);
                if (ReferenceEquals(baseScope, top))
                {
                    baseScope = null;
                }
                DispatchPop(top);
                ScopeTraceSink?.Invoke("pop " + top.Name);
            }
        }

        /// <summary>
        /// Offers one key event to the stack, top-down. A claim is eligible when its scope is live,
        /// its action has a matching binding, and its when-guard passes; the first non-propagate
        /// eligible claim consumes, while propagate claims run and let the walk continue. A live
        /// modal scope masks everything beneath it except action ids on its passlist. Returns true
        /// when consumed; unconsumed events fall through to vanilla untouched.
        /// </summary>
        public bool Dispatch(KeyEventSnapshot e, ActionCatalog catalog, out string consumedActionId)
        {
            consumedActionId = null;
            if (catalog == null || scopes.Count == 0)
            {
                return false;
            }

            // Handlers may push/pop scopes: walk a snapshot, skipping any that leave mid-walk.
            FocusScope[] walk = scopes.ToArray();
            HashSet<string> passFilter = null;
            bool restricted = false;

            for (int i = walk.Length - 1; i >= 0; i--)
            {
                FocusScope scope = walk[i];
                if (!scopes.Contains(scope))
                {
                    continue;
                }
                if (!scope.IsLive)
                {
                    continue;
                }

                IReadOnlyList<FocusScope.ClaimEntry> claims = scope.Claims;
                for (int c = 0; c < claims.Count; c++)
                {
                    FocusScope.ClaimEntry claim = claims[c];
                    if (restricted && (passFilter == null || !passFilter.Contains(claim.ActionId)))
                    {
                        continue;
                    }
                    InputAction action;
                    if (!catalog.TryGet(claim.ActionId, out action))
                    {
                        throw new InvalidOperationException(
                            "Scope '" + scope.Name + "' claims unregistered action '" + claim.ActionId + "'.");
                    }
                    if (!AnyBindingMatches(action, e))
                    {
                        continue;
                    }
                    if (claim.When != null && !claim.When())
                    {
                        continue;
                    }
                    ShellKeyboardOrigin.Begin();
                    try
                    {
                        claim.Handler(e);
                    }
                    catch (Exception ex)
                    {
                        // A handler that throws has still CLAIMED the key: an escaping exception
                        // would skip the caller's Use(), and the surviving event would reach
                        // whatever vanilla binds the same key to.
                        HandlerErrorSink?.Invoke(action.Id, ex);
                    }
                    finally
                    {
                        ShellKeyboardOrigin.End();
                    }
                    if (!claim.Propagate)
                    {
                        consumedActionId = action.Id;
                        return true;
                    }
                }

                if (scope.IsModal)
                {
                    HashSet<string> pass = scope.PassList;
                    if (pass == null || pass.Count == 0)
                    {
                        return false;
                    }
                    if (passFilter == null)
                    {
                        passFilter = new HashSet<string>(pass);
                    }
                    else
                    {
                        passFilter.IntersectWith(pass);
                        if (passFilter.Count == 0)
                        {
                            return false;
                        }
                    }
                    restricted = true;
                }
            }
            return false;
        }

        /// <summary>
        /// Offers a typed character to the stack, top-down: the first live scope with a consuming
        /// <see cref="FocusScope.CharSink"/> wins, and a live modal scope blocks everything beneath
        /// it — the passlist does not apply to text.
        /// </summary>
        public bool OfferChar(char c)
        {
            FocusScope[] walk = scopes.ToArray();
            for (int i = walk.Length - 1; i >= 0; i--)
            {
                FocusScope scope = walk[i];
                if (!scopes.Contains(scope) || !scope.IsLive)
                {
                    continue;
                }
                ICharSink sink = scope.CharSink;
                if (sink != null)
                {
                    ShellKeyboardOrigin.Begin();
                    try
                    {
                        if (sink.HandleChar(c))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        ShellKeyboardOrigin.End();
                    }
                }
                if (scope.IsModal)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// The scope whose <see cref="FocusScope.CharSink"/> would get first offer of a character
        /// right now, or null. Mirrors <see cref="OfferChar"/>'s walk exactly without offering one.
        /// </summary>
        public FocusScope TopCharSinkScope
        {
            get
            {
                FocusScope[] walk = scopes.ToArray();
                for (int i = walk.Length - 1; i >= 0; i--)
                {
                    FocusScope scope = walk[i];
                    if (!scopes.Contains(scope) || !scope.IsLive)
                    {
                        continue;
                    }
                    if (scope.CharSink != null)
                    {
                        return scope;
                    }
                    if (scope.IsModal)
                    {
                        return null;
                    }
                }
                return null;
            }
        }

        /// <summary>Human-readable stack state for logging and the dev bridge.</summary>
        public string DebugDump()
        {
            if (scopes.Count == 0)
            {
                return "FocusStack: (empty)";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("FocusStack (top-down):");
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                FocusScope scope = scopes[i];
                sb.Append("\n  [").Append(i).Append("] ").Append(scope.Name).Append(" (");
                if (ReferenceEquals(scope, baseScope))
                {
                    sb.Append("base, ");
                }
                sb.Append(scope.IsModal ? "modal" : "non-modal");
                sb.Append(", ").Append(scope.IsLive ? "live" : "shadow");
                sb.Append(", claims=").Append(scope.Claims.Count);
                sb.Append(")");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The single pop dispatch: <see cref="FocusScope.OnPop"/> then the registered teardowns,
        /// which run even when OnPop throws and whether or not an override called base.
        /// </summary>
        private static void DispatchPop(FocusScope scope)
        {
            try
            {
                scope.OnPop();
            }
            finally
            {
                scope.RunPopTeardowns();
            }
        }

        private static bool AnyBindingMatches(InputAction action, KeyEventSnapshot e)
        {
            IReadOnlyList<KeyChord> bindings = action.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Matches(e))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
