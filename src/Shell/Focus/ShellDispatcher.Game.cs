using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shadow scope mirroring <see cref="TextInputManager"/>'s modal text-edit session onto
    /// the focus stack, so the stack always reflects that a text session owns the keyboard.
    /// Not live: the dispatcher consumes the session's keys directly.
    /// </summary>
    public sealed class TextSessionScope : FocusScope
    {
        public override string Name
        {
            get { return "text-input"; }
        }
    }

    /// <summary>
    /// Keeps the text-session shadow scope in lockstep with
    /// <see cref="TextInputManager.IsActive"/>, reconciled every OnGUI pass. Push/Pop are
    /// no-ops when already in the wanted state, so this is self-healing after a
    /// boundary ClearToBase. An active session re-floats above any scope pushed later.
    /// </summary>
    internal static class TextSessionScopeMirror
    {
        private static readonly TextSessionScope scope = new TextSessionScope();

        public static void Reconcile()
        {
            if (TextInputManager.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// The shell input dispatcher: one UIRootOnGUI prefix that keeps the focus stack
    /// reconciled and offers each KeyDown to the stack before any other handler sees it.
    /// It runs at Priority.First + 50, after KeyRemapPatch (layout remap, Cmd to Ctrl,
    /// which rewrites Event.current in place) and ahead of every screen-specific prefix.
    /// Consuming here (<c>Event.current.Use()</c>) makes every later prefix inert for that
    /// event; an event no live scope claims and no menu owns falls through untouched to
    /// vanilla. The modal swallow is native to this dispatcher: while
    /// <see cref="ShellGuards.MenuOwnsInput"/> is true, every KeyDown/KeyUp that
    /// <see cref="FocusStack.Dispatch"/> does not claim is consumed here.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    public static class ShellDispatcherPatch
    {
        // Opener-twin-char suppression. A letter-key opener synthesizes a twin char event on
        // the next dispatcher pass, which the freshly opened scope's CharSink would eat as
        // typeahead; this stamp is required, not optional.
        private static FocusScope lastCharSinkScope;
        private static int charSinkChangedFrame = -1;

        // QA marker chord char-twin suppression: on a US layout Shift+Backquote arrives as two
        // same-frame KeyDown events (BackQuote+shift, and keyCode=None with '~'), so
        // RouteCharacterEvent drops the '~' instead of feeding a live typeahead search.
        private static int qaMarkerFrame = -1;

        /// <summary>
        /// The scope-side "a text session owns the keyboard" signal: live scopes stand down
        /// and hold deferred announcements while it is true. It does not park the dispatcher,
        /// which consumes the modal text session directly (see <see cref="Prefix"/>).
        /// </summary>
        internal static bool LegacyKeyboardOverlayActive()
        {
            return TextInputManager.IsActive;
        }

        /// <summary>The stamp catches Unity's BackQuote-first twin; the physical read, 1.7's '~'-first one.</summary>
        private static bool IsQaMarkerTwin(Event e)
        {
            return Time.frameCount == qaMarkerFrame
                || (FlightRecorder.Active && e.shift && KeyboardHelper.IsPhysicallyDown(KeyCode.BackQuote));
        }

        /// <summary>
        /// True on the exact frame the char-sink owner changed to a non-null scope; see the
        /// stamp in <see cref="Prefix"/>.
        /// </summary>
        private static bool SuppressCharOfferThisFrame()
        {
            return charSinkChangedFrame == Time.frameCount;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 50)]
        public static void Prefix()
        {
            try
            {
                // A live capture pass still open on a later frame proves its own EndPass never
                // ran (the window closed mid-pass) and would grow unbounded. Must run every
                // OnGUI pass, as must the two pumps below.
                WidgetCapture.EndLeakedLivePass();

                BulkSoundQueue.Update();

                // Settle poll for async-rewritten mod text a reader deferred; internally
                // guarded to check at most once per engine frame.
                AsyncTextStability.Tick();

                // Mirror reconciliation walks MirrorReconcileOrder's array; that order is
                // load-bearing and hand-maintained (see its doc comment). Mirrors re-float
                // their scope unconditionally, so without this bracket two stacked mirror
                // scopes re-float above each other every pass, each firing a fresh
                // OnFocus/AfterFocusDispatch — a same-line speech flood. The bracket lets the
                // walk reorder freely and fires focus events once, only if the net top changed.
                MirrorReconcileEntry[] mirrors = MirrorReconcileOrder.Entries;
                FocusStack.BeginReconcile();
                try
                {
                    for (int mirrorIndex = 0; mirrorIndex < mirrors.Length; mirrorIndex++)
                    {
                        mirrors[mirrorIndex].Reconcile();
                    }
                }
                finally
                {
                    FocusStack.EndReconcile();
                }

#if DEBUG
                // Runs at the reconcile tail so the stack it reads is the frame's settled one.
                VisualSurfaceTripwire.Check();
#endif

                // Releases any native IMGUI control a mod focused directly (e.g. a search box
                // on a scope-driven vanilla dialog) that would otherwise eat keys. After
                // reconcile so a scope attached this pass counts in MenuOwnsInput, before
                // dispatch so the release is in force downstream.
                ShellTextFocus.ReleaseForeignFocus();

                // Polls here rather than at the map tick site: it must also run on menus with
                // no map loaded. Internally gated to Repaint.
                UiPointerFollow.Tick();

                // Sample the char-sink owner once per pass, after every mirror has reconciled.
                // A change to a non-null owner means a menu just opened, so this frame's char
                // events are the opener keystroke's twins and must not seed the new menu's
                // typeahead buffer. A change to null does not stamp: no new owner to protect.
                FocusScope sinkScope = FocusStack.TopCharSinkScope;
                if (!ReferenceEquals(sinkScope, lastCharSinkScope))
                {
                    lastCharSinkScope = sinkScope;
                    if (sinkScope != null)
                    {
                        charSinkChangedFrame = Time.frameCount;
                    }
                }

                // Must run on EVERY OnGUI pass, not just KeyDown; see ImeFunnel.PumpEveryPass.
                ImeFunnel.PumpEveryPass();

                Event e = Event.current;
                if (e == null)
                {
                    return;
                }

                // Diagnostic only, deliberately ahead of every gate below: the one place that
                // sees a mouse press before any window handles it. Never touches the event.
                FlightRecorder.RecordPointerFrame();
                FlightRecorder.RecordMouseDown(e);
                FlightRecorder.RecordMouseDrag(e);

                // The KeyUp half of the modal swallow: vanilla sees a used event identically
                // wherever it was consumed, so swallowing KeyUp this early is safe.
                if (e.type == EventType.KeyUp && ShellGuards.MenuOwnsInput())
                {
                    e.Use();
                    return;
                }

                if (e.type != EventType.KeyDown)
                {
                    return;
                }

                // Interrupt speech on key press for backends that don't do it themselves (macOS
                // AVSpeechSynthesizer without VoiceOver). Must stay ahead of dispatch, or a
                // claim that consumes the event leaves the interrupt unfired.
                if (TolkHelper.ShouldInterruptOnKeyPress)
                {
                    TolkHelper.StopSpeech();
                }

                // Ground-truth recorder for every KeyDown: raw event, effective modifiers,
                // disposition. In DEBUG also the bridge's rolling ShellDev.DumpEventTrace().
                string trace = !FlightRecorder.KeyTraceWanted ? null : Time.frameCount
                    + " key=" + e.keyCode
                    + " ch=" + (int)e.character
                    + " mod=" + e.modifiers
                    + " eff=" + (KeyboardHelper.IsCtrlHeld ? "C" : "") + (e.shift ? "S" : "") + (KeyboardHelper.IsAltHeld ? "A" : "")
                    + (KeyboardHelper.InjectionOverrideActive ? " inj" : "")
                    + " top=" + (FocusStack.Top == null ? "-" : FocusStack.Top.Name);

                // Alt (Option on macOS) + the vanilla screenshot binding, read live so a
                // rebound key carries the chord, toggles the flight recorder. BEFORE the
                // character routing: a live text session (browsing the error log's stack
                // trace) must not eat the chord, and the screenshot key types no character.
                // ScreenshotTakerAltMaskPatch stops vanilla's own screenshot.
                if (e.keyCode != KeyCode.None
                    && RimWorld.KeyBindingDefOf.TakeScreenshot != null
                    && e.keyCode == RimWorld.KeyBindingDefOf.TakeScreenshot.MainKey
                    && KeyboardHelper.IsAltHeld && !e.shift && !KeyboardHelper.IsCtrlHeld)
                {
                    if (trace != null)
                    {
                        FlightRecorder.RecordKey(trace + " -> flight-recorder-toggle");
                    }
                    FlightRecorder.Toggle();
                    e.Use();
                    return;
                }

                // Character-consuming routing; see RouteCharacterEvent for the stage order.
                if (RouteCharacterEvent(e, out string routedStage))
                {
                    if (trace != null)
                    {
                        FlightRecorder.RecordKey(trace + " -> " + routedStage);
                    }
                    e.Use();
                    return;
                }

                // Shift+Backquote drops a numbered marker into the QA flight recorder.
                // Deliberately after RouteCharacterEvent (a live text session still types the
                // tilde) and before scope dispatch (fires identically under every scope, modal
                // included). Not in ShellActionInventory: recorder tooling, not rebindable.
                if (e.keyCode == KeyCode.BackQuote && e.shift
                    && !KeyboardHelper.IsCtrlHeld && !KeyboardHelper.IsAltHeld
                    && FlightRecorder.Active)
                {
                    qaMarkerFrame = Time.frameCount;
                    FlightRecorder.MarkFromKeyboard();
                    if (trace != null)
                    {
                        FlightRecorder.RecordKey(trace + " -> qa-marker");
                    }
                    e.Use();
                    return;
                }

                // Tab-chord platform normalization. IsCtrlHeld substitutes Option for Ctrl on
                // macOS when the key is Tab, but Option is also Alt, so Option+Tab snapshots as
                // ctrl AND alt and no exact-match Ctrl+Tab chord would ever fire on Mac.
                // Collapsing to ctrl-only is unambiguous: no action registers Ctrl+Alt+Tab. The
                // IsMacOS gate leaves a real Windows/Linux Ctrl+Alt+Tab untouched.
                bool ctrlHeld = KeyboardHelper.IsCtrlHeld;
                bool altHeld = KeyboardHelper.IsAltHeld;
                if (e.keyCode == KeyCode.Tab && NativeLibraryLoader.IsMacOS && ctrlHeld && altHeld)
                {
                    altHeld = false;
                }

                KeyEventSnapshot snapshot = new KeyEventSnapshot(
                    e.keyCode,
                    ctrlHeld,
                    e.shift,
                    altHeld,
                    e.character);

                string consumedActionId;
                if (FocusStack.Dispatch(snapshot, out consumedActionId))
                {
                    if (trace != null)
                    {
                        FlightRecorder.RecordKey(trace + " -> " + consumedActionId);
                    }
                    PlayCursorMoveTick(consumedActionId);
                    e.Use();
                }
                else if (ShellGuards.MenuOwnsInput())
                {
                    // The modal swallow. Dozens of scopes deliberately under-claim and depend on
                    // it eating every unclaimed KeyDown while a menu owns input, pre-game
                    // included — never weaken the predicate.
                    if (trace != null)
                    {
                        FlightRecorder.RecordKey(trace + " -> modal-swallow");
                    }
                    e.Use();
                }
                else if (trace != null)
                {
                    FlightRecorder.RecordKey(trace + " -> unclaimed");
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Shell dispatcher error", ex);
            }
        }

        private static int lastCursorTickFrame = -1;

        /// <summary>
        /// Plays vanilla's mouseover click on a keyboard cursor move; purely additive, no
        /// existing sound is suppressed. The frame stamp is a local "already ticked this
        /// frame" counter for key repeat, not cross-component frame coordination.
        /// </summary>
        private static void PlayCursorMoveTick(string actionId)
        {
            if (!SharedMenuGrammar.IsCursorMove(actionId))
                return;
            NotifyCursorMoved();
        }

        /// <summary>
        /// The same tick for a handler that moved the cursor by a route the shared grammar
        /// cannot classify. The frame stamp makes the dispatcher's own later call a no-op.
        /// </summary>
        internal static void NotifyCursorMoved()
        {
            if (lastCursorTickFrame == Time.frameCount)
                return;
            if (SoundDefOf.Mouseover_Standard == null)
                return;
            lastCursorTickFrame = Time.frameCount;
            SoundDefOf.Mouseover_Standard.PlayOneShotOnCamera();
        }

        /// <summary>
        /// The character-consuming stage of <see cref="Prefix"/>'s routing chain, in order:
        /// IME composition funnel, CJK menu-search prompt, modal text-edit session, live
        /// CharSinks. One method so <see cref="ShellDev.InjectText"/> replays a synthetic
        /// character through the real path instead of a second router that would drift.
        /// Returns true when a stage consumed the event, with <paramref name="stage"/> naming
        /// it; the stage's own side effect runs here, but <c>e.Use()</c> stays the caller's.
        /// </summary>
        internal static bool RouteCharacterEvent(Event e, out string stage)
        {
            if (ImeFunnel.TryRouteKeyDown(e))
            {
                stage = "ime-divert";
                return true;
            }

            if (ImeFunnel.HandleMenuSearchKeyDown(e))
            {
                stage = "menu-search";
                return true;
            }

            // The modal text session takes ALL keystrokes, and the event is consumed
            // unconditionally — even keys the controller ignored — so window patches hooking
            // DoWindowContents cannot grab a key out from under the edit session.
            // MarkHandledThisFrame lets the OnAcceptKeyPressed/OnCancelKeyPressed patches block
            // the game's Enter/Escape even after HandleEnter clears IsActive.
            if (TextInputManager.IsActive)
            {
                TextInputManager.Active.HandleEvent(e);
                TextInputManager.MarkHandledThisFrame();
                stage = "text-session";
                return true;
            }

            // Typed characters reach live char sinks (text/typeahead) before any chord
            // matching, in stack order, minus four twin-event gaps: a held action modifier
            // means the char is a shortcut's twin (AZERTY-class layouts deliver Alt+letter
            // twins US layouts don't, and ApplyGlobalRemap rewrites keyCode but not
            // Event.current.character), the ']' keypress's follow-up character, an
            // opener keystroke's twin, and the QA marker chord's '~'. A skipped char falls
            // through to Dispatch and then the modal swallow.
            char ch = e.character;
            if (ch != '\0' && !char.IsControl(ch)
                && !KeyboardHelper.IsAltHeld && !KeyboardHelper.IsCtrlHeld
                && !KeyboardHelper.WasRightBracketThisFrame
                && !SuppressCharOfferThisFrame()
                && !(ch == '~' && IsQaMarkerTwin(e))
                && FocusStack.OfferChar(ch))
            {
                stage = "char-consumed";
                return true;
            }

            stage = null;
            return false;
        }
    }
}
