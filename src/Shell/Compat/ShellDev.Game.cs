#if DEBUG
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dev-bridge harness for scripted end-to-end input tests against the live game. Named
    /// <c>ShellDev</c> rather than <c>Shell</c> so it does not collide with the
    /// <c>RimWorldAccess.Shell</c> namespace.
    /// <see cref="Inject"/> builds one synthetic KeyDown <see cref="Event"/> and replays it through
    /// the accessibility patch chain synchronously on the calling thread, so a single curl call can
    /// drive a navigation sequence and read back what was consumed and what was spoken.
    /// <code>
    /// curl -s --data 'RimWorldAccess.Shell.ShellDev.Dump()' http://127.0.0.1:8787/eval
    /// curl -s --data 'RimWorldAccess.Shell.ShellDev.Inject("Escape")' http://127.0.0.1:8787/eval
    /// curl -s --data 'RimWorldAccess.Shell.ShellDev.InjectAndCapture("F2, DownArrow, Escape")' http://127.0.0.1:8787/eval
    /// </code>
    /// LIMITATIONS (read before trusting a negative result):
    /// - Covers exactly two stops, in real priority order: the shell dispatcher
    ///   (<see cref="ShellDispatcherPatch"/>, which includes the native modal swallow) and the
    ///   windowless-dialog handler.
    /// - Does NOT run the side-door UIRootOnGUI prefixes sitting between those two in the real chain
    ///   (WorkMenuPatch, AreaPatch), nor any other handler sharing the windowless-dialog handler's
    ///   priority tier (storage-settings, building-inspect) — a chord one of those owns misreports
    ///   here as "fell through".
    /// - Does NOT run KeyRemapPatch (layout remap / Cmd to Ctrl); moot, since a
    ///   <see cref="KeyChord"/> is already normalized and layout-independent.
    /// - Does NOT run vanilla window key handling (Window.OnCancelKeyPressed / OnAcceptKeyPressed and
    ///   the rest of the real window stack). A REAL blind spot, not a technicality: each open
    ///   window's deferred InnerWindowOnGUI re-tests the Accept/Cancel bindings on the raw event,
    ///   where the dispatcher's Use() is invisible (see WindowKeyRouter), so a real keypress can
    ///   close a window an injected one leaves open. Smoke tests must simulate that pass explicitly:
    ///   call Find.WindowStack.Notify_PressedAccept() / Notify_PressedCancel() right after injecting
    ///   Enter/Escape and assert the surface survived.
    /// - A handler that calls GUI/GUILayout APIs may throw when invoked outside a real OnGUI pass.
    ///   That exception is deliberately NOT caught: it propagates out of <see cref="Inject"/> and
    ///   surfaces in the bridge response.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>Dumps the live focus stack: scope names top to bottom, live/shadow, claims.</summary>
        public static string Dump() => FocusStack.DebugDump();

        private const int EventTraceCap = 300;
        private static readonly List<string> eventTrace = new List<string>();

        /// <summary>
        /// Appends one line to the rolling keyboard-event trace; <see cref="FlightRecorder.RecordKey"/>
        /// tees every dispatcher KeyDown here, real or injected. The ground-truth recorder for "my key
        /// did nothing" reports: run the repro, then read <see cref="DumpEventTrace"/> over the bridge.
        /// </summary>
        internal static void TraceEvent(string line)
        {
            if (eventTrace.Count >= EventTraceCap)
            {
                eventTrace.RemoveAt(0);
            }
            eventTrace.Add(line);
        }

        /// <summary>
        /// The rolling keyboard-event trace, newest last, one event per line, cleared unless
        /// <paramref name="clear"/> is false. Line shape:
        /// "frame key=Return ch=10 mod=None eff=- top=pause-menu -&gt; menus.activate"; "inj" marks
        /// events synthesized by <see cref="Inject"/>.
        /// </summary>
        public static string DumpEventTrace(bool clear = true)
        {
            string report = eventTrace.Count == 0
                ? "(event trace empty)"
                : string.Join("\n", eventTrace.ToArray());
            if (clear)
            {
                eventTrace.Clear();
            }
            return report;
        }

        /// <summary>Simulates one physical KeyDown through the accessibility patch chain and returns a one-line summary. See the class remarks for which patches run and which are skipped.</summary>
        /// <param name="chordText">A <see cref="KeyChord"/>-parseable chord, e.g. "Alt+M", "Ctrl+Shift+G", "Escape".</param>
        /// <param name="character">
        /// The character the key would have produced, needed to exercise typed-character paths
        /// (typeahead, text entry), e.g. Inject("A", 'a'). Defaults to '\0', correct for plain chords.
        /// </param>
        public static string Inject(string chordText, char character = '\0')
        {
            KeyChord chord = KeyChord.Parse(chordText);

            Event prev = Event.current;
            Event synthetic = new Event();
            synthetic.type = EventType.KeyDown;
            synthetic.keyCode = chord.Key;
            synthetic.character = character;

            EventModifiers modifiers = EventModifiers.None;
            if (chord.Ctrl)
            {
                modifiers |= EventModifiers.Control;
            }
            if (chord.Shift)
            {
                modifiers |= EventModifiers.Shift;
            }
            if (chord.Alt)
            {
                modifiers |= EventModifiers.Alt;
            }
            KeyboardHelper.SetEventModifiers(synthetic, modifiers);

            Event.current = synthetic;
            KeyboardHelper.InjectionOverrideActive = true;
            KeyboardHelper.InjectedCtrl = chord.Ctrl;
            KeyboardHelper.InjectedAlt = chord.Alt;

            try
            {
                // Mirrors real Harmony prefix semantics: a bool prefix returning false stops every
                // lower-priority patch (and the original method) for this event. The native modal
                // swallow runs inside ShellDispatcherPatch.Prefix itself.
                ShellDispatcherPatch.Prefix();
            }
            finally
            {
                KeyboardHelper.InjectionOverrideActive = false;
                KeyboardHelper.InjectedCtrl = false;
                KeyboardHelper.InjectedAlt = false;
                Event.current = prev;
            }

            bool consumed = synthetic.type != EventType.KeyDown;
            return chordText + " -> " + (consumed ? "consumed" : "fell through") + "; stack: " + FocusStack.Count;
        }

        /// <summary>
        /// Injects a comma-separated sequence of chords (each trimmed, e.g. "F2, DownArrow, Escape")
        /// while capturing everything spoken across the sequence: one line per chord (the
        /// <see cref="Inject"/> summary), then a "speech:" section listing every sanitized utterance in
        /// speak order. A single comma-separated string rather than a params array, because the dev
        /// bridge passes C# script text as one HTTP body.
        /// </summary>
        public static string InjectAndCapture(string chordsCommaSeparated)
        {
            string[] tokens = (chordsCommaSeparated ?? string.Empty).Split(',');
            List<string> summaries = new List<string>();
            string[] spoken;

            TolkHelper.BeginCapture();
            try
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    string chord = tokens[i].Trim();
                    if (chord.Length == 0)
                    {
                        continue;
                    }
                    summaries.Add(Inject(chord));
                }
            }
            finally
            {
                // Always close the capture, even if a chord threw, so a failed sequence still reports
                // what was said and TolkHelper.IsCapturing is never left stuck true.
                spoken = TolkHelper.EndCapture();
            }

            StringBuilder report = new StringBuilder();
            foreach (string summary in summaries)
            {
                report.AppendLine(summary);
            }
            report.AppendLine("speech:");
            foreach (string line in spoken)
            {
                report.Append("    ").AppendLine(line);
            }
            return report.ToString();
        }
    }
}
#endif
