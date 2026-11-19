#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's multi-frame sequence engine. The problem <see cref="InjectText"/>'s own header
    /// documents — an <see cref="Inject"/> sequence runs synchronously inside one drained main-thread
    /// action, so a chord that opens a menu and the very next chord/text race the SAME render frame the
    /// menu's first frame has (Index -1, a stray Enter no-ops) and the opener-twin-char suppression
    /// stamp eats the first typed character — this engine fixes it by spacing steps across REAL frames:
    /// <see cref="AdvanceSequence"/> runs once per <c>UIRootOnGUI</c> pass
    /// (<see cref="RimWorldAccess.DevBridge.DevBridgeDrainPatch"/>), so a "wait N" between two
    /// steps is N real frames, not zero.
    ///
    /// Script grammar: semicolon-separated tokens, each one of a chord (anything
    /// <see cref="KeyChord.Parse"/> accepts, e.g. "F12", "Ctrl+Shift+G"), "wait N" (an explicit
    /// frame count, added to the default), or "text:abc" (one character per step; physical presses
    /// in real mode, else <see cref="InjectText"/>). The default wait applies BETWEEN two action
    /// steps without an explicit "wait"; the first action fires as soon as
    /// <see cref="AdvanceSequence"/> is first called unless the script opens with a "wait".
    /// </summary>
    public static partial class ShellDev
    {
        private enum StepKind { Chord, Text, RealModifierDown, RealModifierUp, RealTap }

        private struct SequenceAction
        {
            public StepKind Kind;
            public string Payload;
            public KeyCode Key;
            public int WaitFramesBefore;
        }

        private static List<SequenceAction> sequenceActions;
        private static int sequenceActionIndex;
        private static int sequenceWaitRemaining;
        private static List<string> sequenceSummaries;
        private static int sequenceStartSpeechStamp;
        private static int sequenceStartTraceTotal;
        private const int RealSettleFrames = 5;
        private static bool sequenceReal;
        private static bool sequenceRunningFlag;

        /// <summary>Last frame <see cref="AdvanceSequence"/> did work — see the multi-pass guard inside it.</summary>
        private static int lastAdvanceFrame = -1;
        private static string sequenceResultValue;

        /// <summary>True while a sequence armed by <see cref="StartSequence"/> is still executing.</summary>
        internal static bool SequenceRunning
        {
            get { return sequenceRunningFlag; }
        }

        /// <summary>Null until the running sequence finishes (or throws); then the full report.</summary>
        internal static string SequenceResult
        {
            get { return sequenceResultValue; }
        }

        // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
        private const string SequenceAlreadyRunningError = "ERROR: a sequence is already running";

        /// <summary>
        /// Parses <paramref name="script"/> and arms it for <see cref="AdvanceSequence"/> to run
        /// one frame at a time. Returns an error string immediately (without arming anything) when
        /// a sequence is already running; returns null on a successful arm — poll
        /// <see cref="SequenceRunning"/>/<see cref="SequenceResult"/> for the outcome.
        /// </summary>
        internal static string StartSequence(string script, int defaultWaitFrames, bool real)
        {
            if (sequenceRunningFlag)
            {
                return SequenceAlreadyRunningError;
            }

            sequenceReal = real && RealInputAvailable;
            sequenceActions = ParseSequenceScript(script, Math.Max(0, defaultWaitFrames), sequenceReal);
            sequenceActionIndex = 0;
            sequenceWaitRemaining = sequenceActions.Count > 0 ? sequenceActions[0].WaitFramesBefore : 0;
            sequenceSummaries = new List<string>();
            sequenceStartSpeechStamp = SpeechRingStamp();
            sequenceStartTraceTotal = eventTraceTotal;
            sequenceResultValue = null;
            sequenceRunningFlag = true;

            if (sequenceActions.Count == 0)
            {
                FinishSequence();
            }
            return null;
        }

        /// <summary>
        /// Called once per real frame (after <c>MainThreadDispatcher.DrainPending</c>). Executes
        /// the next due step, or counts down its wait by one frame. Any step throwing finishes the
        /// sequence immediately with what ran so far — it never leaves <see cref="SequenceRunning"/>
        /// stuck true.
        /// </summary>
        internal static void AdvanceSequence()
        {
            if (!sequenceRunningFlag)
            {
                return;
            }
            // UIRootOnGUI runs SEVERAL passes per frame (Layout, Repaint, one per queued input
            // event), and this is called from its postfix — without this guard a "wait N" melts
            // to a fraction of N real frames and two zero-wait steps can execute in the SAME
            // frame, reintroducing the exact same-frame race this engine exists to dodge.
            if (Time.frameCount == lastAdvanceFrame)
            {
                return;
            }
            lastAdvanceFrame = Time.frameCount;
            if (sequenceWaitRemaining > 0)
            {
                sequenceWaitRemaining--;
                return;
            }
            if (sequenceActionIndex >= sequenceActions.Count)
            {
                FinishSequence();
                return;
            }

            SequenceAction action = sequenceActions[sequenceActionIndex];
            try
            {
                string summary = RunStep(action);
                if (summary != null)
                {
                    sequenceSummaries.Add(summary);
                }
            }
            catch (Exception ex)
            {
                sequenceSummaries.Add(action.Payload + " -> threw " + ex.GetType().Name);
            }

            sequenceActionIndex++;
            if (sequenceActionIndex < sequenceActions.Count)
            {
                sequenceWaitRemaining = sequenceActions[sequenceActionIndex].WaitFramesBefore;
            }
            else if (sequenceReal)
            {
                sequenceWaitRemaining = RealSettleFrames;
            }
            else
            {
                FinishSequence();
            }
        }

        private static string RunStep(SequenceAction action)
        {
            switch (action.Kind)
            {
                case StepKind.Text: return InjectText(action.Payload);
                case StepKind.RealModifierDown: RealModifier(action.Key, down: true); return null;
                case StepKind.RealModifierUp: RealModifier(action.Key, down: false); return null;
                case StepKind.RealTap: return RealTap(action.Key, action.Payload);
                default: return Inject(action.Payload);
            }
        }

        private static void FinishSequence()
        {
            var sb = new StringBuilder();
            sb.Append(sequenceReal ? "mode: real keys\n" : "mode: synthetic\n");
            for (int i = 0; i < sequenceSummaries.Count; i++)
            {
                sb.Append(sequenceSummaries[i]);
                if (sequenceSummaries[i].Length == 0 || sequenceSummaries[i][sequenceSummaries[i].Length - 1] != '\n')
                {
                    sb.Append('\n');
                }
            }
            if (sequenceReal)
            {
                sb.Append("keys:\n").Append(EventTraceSince(sequenceStartTraceTotal)).Append('\n');
            }
            sb.Append("speech:\n").Append(SpeechSince(sequenceStartSpeechStamp));

            sequenceResultValue = sb.ToString();
            sequenceRunningFlag = false;
            sequenceActions = null;
            sequenceSummaries = null;
        }

        private static List<SequenceAction> ParseSequenceScript(string script, int defaultWaitFrames, bool real)
        {
            var actions = new List<SequenceAction>();
            string[] tokens = (script ?? string.Empty).Split(';');
            int pendingWait = 0;
            bool waitExplicit = false;
            bool first = true;

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0)
                {
                    continue;
                }
                if (TryParseWaitToken(token, out int frames))
                {
                    pendingWait += frames;
                    waitExplicit = true;
                    continue;
                }

                int waitBefore = waitExplicit ? pendingWait : (first ? 0 : defaultWaitFrames);
                bool isText = token.StartsWith("text:", StringComparison.OrdinalIgnoreCase);
                string payload = isText ? token.Substring(5) : token;
                if (isText)
                {
                    // One character per FRAME: typeahead's per-keystroke auto-expand rebuilds the
                    // search space, and a whole word inside one frame races that rebuild ("weap"
                    // typed same-frame found nothing). Two frames apart mirrors a fast typist.
                    for (int c = 0; c < payload.Length; c++)
                    {
                        int charWait = c == 0 ? waitBefore : 2;
                        if (real && TryKeyForCharacter(payload[c], out KeyCode charKey, out bool shift))
                        {
                            AddRealPress(actions, charKey, shift ? new[] { KeyCode.LeftShift } : new KeyCode[0], payload[c].ToString(), charWait);
                        }
                        else
                        {
                            actions.Add(new SequenceAction { Kind = StepKind.Text, Payload = payload[c].ToString(), WaitFramesBefore = charWait });
                        }
                    }
                }
                else if (real && KeyChord.TryParse(payload, out KeyChord chord))
                {
                    var modifiers = new List<KeyCode>();
                    if (chord.Ctrl) modifiers.Add(KeyCode.LeftControl);
                    if (chord.Alt) modifiers.Add(KeyCode.LeftAlt);
                    if (chord.Shift) modifiers.Add(KeyCode.LeftShift);
                    AddRealPress(actions, chord.Key, modifiers.ToArray(), payload, waitBefore);
                }
                else
                {
                    actions.Add(new SequenceAction { Kind = StepKind.Chord, Payload = payload, WaitFramesBefore = waitBefore });
                }

                pendingWait = 0;
                waitExplicit = false;
                first = false;
            }
            return actions;
        }

        /// <summary>Modifiers go down the frame before the key and up the frame after; ImguiDispatch reads them live.</summary>
        private static void AddRealPress(List<SequenceAction> actions, KeyCode key, KeyCode[] modifiers, string label, int waitBefore)
        {
            for (int m = 0; m < modifiers.Length; m++)
            {
                actions.Add(new SequenceAction { Kind = StepKind.RealModifierDown, Key = modifiers[m], WaitFramesBefore = m == 0 ? waitBefore : 0 });
            }
            actions.Add(new SequenceAction { Kind = StepKind.RealTap, Key = key, Payload = label, WaitFramesBefore = modifiers.Length > 0 ? 1 : waitBefore });
            for (int m = 0; m < modifiers.Length; m++)
            {
                actions.Add(new SequenceAction { Kind = StepKind.RealModifierUp, Key = modifiers[m], WaitFramesBefore = m == 0 ? 1 : 0 });
            }
        }

        private static bool TryParseWaitToken(string token, out int frames)
        {
            frames = 0;
            if (!token.StartsWith("wait", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string rest = token.Substring(4).Trim();
            return int.TryParse(rest, out frames);
        }
    }
}
#endif
