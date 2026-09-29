using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Platform;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// QA flight recorder: appends every keypress resolution, every spoken utterance and every
    /// focus-scope push/pop to a plain-text log with wall-clock timestamps, so a dictated QA
    /// transcript can be correlated against what the mod did.
    /// Compiled into every build and always manual: records only between explicit start/stop.
    /// Alt (Option on macOS) + the vanilla screenshot key toggles
    /// recording from anywhere; stopping announces the saved path and offers
    /// <see cref="Dialog_RecordingSaved"/>. Files land one per recording at
    /// <c>&lt;SaveDataFolder&gt;/RimWorldAccess/FlightRecorder/flight-recorder-yyyyMMdd-HHmmss.log</c>; the writer is
    /// append-only and autoflushed, so a crash loses nothing already written. Line shape:
    /// <c>HH:mm:ss.fff [kind] line</c>, kind being "key", "press", "pointer", "click", "drag",
    /// "speech", "scope", "mark", "capture", "filter" or "floatmenu". "press" is a PHYSICAL mouse
    /// press Unity polled and "click" one the game's GUI received, so a press with no click beside it
    /// never reached RimWorld; "pointer" lines mark every change in host focus and in whether the
    /// pointer is on the game's surface. A "scope" line reads "push"/"pop"/"attach" for genuine stack
    /// transitions and "refloat" for a mirror re-floating a scope it already owns; a per-pass mirror
    /// re-float is traced only when it changes the effective top.
    /// </summary>
    public static class FlightRecorder
    {
        private static readonly object traceLock = new object();
        private static StreamWriter writer;
        private static string tracePath;
        private static int traceCount;
        private static bool traceFailed;
        private static bool active;

        /// <summary>
        /// True while entries are being accepted. Hot-path callers gate their string building on
        /// this; a stale read costs at most one line at a start/stop boundary.
        /// </summary>
        internal static bool Active
        {
            get { return active && !traceFailed; }
        }

        /// <summary>
        /// The toggle chord in spoken form ("Option+F10" on macOS, "Alt+F12" default elsewhere),
        /// built from the player's CURRENT screenshot binding so rebinding it updates every
        /// reference. For settings descriptions and announcements.
        /// </summary>
        internal static string ToggleChordDisplay()
        {
            KeyCode key = KeyBindingDefOf.TakeScreenshot != null
                ? KeyBindingDefOf.TakeScreenshot.MainKey
                : KeyCode.F12;
            return KeyChord.Of(key, alt: true).DisplayLabel;
        }

        /// <summary>
        /// Whether the dispatcher should build its per-KeyDown trace line. DEBUG always wants it
        /// (the dev bridge's rolling event trace reads it even while the recorder is stopped).
        /// </summary>
        internal static bool KeyTraceWanted
        {
            get
            {
#if DEBUG
                return true;
#else
                return Active;
#endif
            }
        }

        /// <summary>
        /// Appends one timestamped line, opening the file lazily on first call. Never throws: a failure
        /// disables further recording (logged once) rather than risking input handling or speech.
        /// </summary>
        internal static void Record(string kind, string line)
        {
            lock (traceLock)
            {
                if (!active || traceFailed)
                {
                    return;
                }
                try
                {
                    if (writer == null)
                    {
                        string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimWorldAccess", "FlightRecorder");
                        Directory.CreateDirectory(dir);
                        tracePath = Path.Combine(dir, "flight-recorder-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                        writer = new StreamWriter(tracePath, true) { AutoFlush = true };
                        writer.WriteLine("# RimWorld Access flight recorder log, recording started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                    writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + kind + "] " + line);
                    traceCount++;
                }
                catch (Exception ex)
                {
                    traceFailed = true;
                    Log.Warning("[RimWorld Access] QA flight recorder failed, disabling: " + ex.Message);
                }
            }
        }

        /// <summary>One window add/remove line naming its caller chain; ImmediateWindows are minted per frame and skipped.</summary>
        internal static void RecordWindowLifecycle(string verb, Window window)
        {
            if (!Active || window is ImmediateWindow)
            {
                return;
            }
            Record("window", verb + " " + window.GetType().FullName + " by " + CallerSummary());
        }

        /// <summary>Up to three <c>Type.Method</c> frames, innermost first, minus the recorder, the mirror, Harmony stubs and WindowStack.</summary>
        private static string CallerSummary()
        {
            var frames = new List<string>();
            var trace = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < trace.FrameCount && frames.Count < 3; i++)
            {
                MethodBase method = trace.GetFrame(i).GetMethod();
                Type type = method != null ? method.DeclaringType : null;
                if (type == null
                    || type == typeof(FlightRecorder)
                    || type == typeof(WindowStack)
                    || type.Name.StartsWith("ScopeForWindow", StringComparison.Ordinal)
                    || method.Name.StartsWith("DMD<", StringComparison.Ordinal)
                    || type.Namespace == null)
                {
                    continue;
                }
                frames.Add(type.Name + "." + method.Name);
            }
            return frames.Count > 0 ? string.Join(" < ", frames) : "unknown";
        }

        /// <summary>One KeyDown disposition line; DEBUG also tees it to the dev bridge's rolling event trace.</summary>
        internal static void RecordKey(string line)
        {
#if DEBUG
            ShellDev.TraceEvent(line);
#endif
            Record("key", line);
        }

        /// <summary>The toggle chord's vehicle: stop when recording, start otherwise.</summary>
        internal static void Toggle()
        {
            if (Active)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Arms the recorder; the file opens lazily on the first line, which the announcement
        /// itself supplies through the speech tee. Clears a previous write failure so a fresh
        /// start may retry.
        /// </summary>
        internal static void StartRecording()
        {
            lock (traceLock)
            {
                traceFailed = false;
                active = true;
            }
            TolkHelper.SpeakData((string)"RimWorldAccess.FlightRecorder.Started".Translate(), SpeechPriority.High);
        }

        /// <summary>
        /// Closes the current recording and tells the player where it landed: through
        /// <see cref="Dialog_RecordingSaved"/> unless suppressed in settings, otherwise spoken.
        /// </summary>
        internal static void StopRecording()
        {
            string savedPath;
            lock (traceLock)
            {
                active = false;
                savedPath = tracePath;
                if (writer != null)
                {
                    try { writer.Dispose(); }
                    catch (Exception ex) { Log.Warning("[RimWorld Access] QA flight recorder close failed: " + ex.Message); }
                }
                writer = null;
                tracePath = null;
                traceCount = 0;
            }
            if (savedPath == null)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.FlightRecorder.Stopped".Translate(), SpeechPriority.High);
                return;
            }
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if ((settings == null || settings.ShowRecordingSavedDialog) && Find.WindowStack != null)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.FlightRecorder.Stopped".Translate(), SpeechPriority.High);
                Find.WindowStack.Add(new Dialog_RecordingSaved(savedPath));
            }
            else
            {
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.FlightRecorder.StoppedWithPath".Translate(savedPath), SpeechPriority.High);
            }
        }

        private static int markerCount;

        /// <summary>
        /// The QA marker key (Shift+Backquote, from the shell dispatcher): drops a numbered "mark" line
        /// into the log so a dictated narration and the trace line up by section, then confirms aloud.
        /// </summary>
        internal static void MarkFromKeyboard()
        {
            int n = ++markerCount;
            Record("mark", "manual marker " + n);
            TolkHelper.SpeakData((string)"RimWorldAccess.FlightRecorder.Marker".Translate(n), SpeechPriority.High);
        }

        /// <summary>Current log path (or a placeholder if none open) and entry count, for the dev bridge.</summary>
        internal static string Status()
        {
            lock (traceLock)
            {
                return (tracePath ?? "(not started)") + ", " + traceCount + " entries";
            }
        }

        /// <summary>
        /// Records every mouse button press the game's GUI receives, so a trace can answer whether a
        /// reported click reached RimWorld at all. Pure observation: nothing here reads or uses the
        /// event. <c>rawType</c>, not <c>type</c>: a press an earlier pass consumed reports
        /// <c>Used</c>, and a missing press is the one fact a mouse diagnosis most needs to trust.
        /// Pair these with the physical-press lines <see cref="RecordPointerFrame"/> writes — a
        /// physical press with no matching "click" is one the GUI never saw.
        /// </summary>
        internal static void RecordMouseDown(Event e)
        {
            if (!Active || e == null || e.rawType != EventType.MouseDown)
            {
                return;
            }
            Record("click", "button=" + e.button
                + " clicks=" + e.clickCount
                + PointerContext()
                + " hover=" + HoverSpeech.DebugSpokenTarget);
        }

        private static bool dragOpen;
        private static Vector2 dragFrom;
        private static int dragStartFrame;
        private static int dragButton;

        /// <summary>
        /// Records the two edges of a drag gesture — the first <c>MouseDrag</c> after a press and the
        /// <c>MouseUp</c> ending it — so a trace tells a drag from a click without a line per frame.
        /// </summary>
        internal static void RecordMouseDrag(Event e)
        {
            if (!Active || e == null)
            {
                return;
            }
            if (e.rawType == EventType.MouseDrag)
            {
                if (dragOpen)
                {
                    return;
                }
                dragOpen = true;
                dragFrom = UI.MousePositionOnUIInverted;
                dragStartFrame = Time.frameCount;
                dragButton = e.button;
                Record("drag", "start button=" + dragButton + PointerContext());
                return;
            }
            if (e.rawType == EventType.MouseUp && dragOpen)
            {
                dragOpen = false;
                Record("drag", "end button=" + dragButton + PointerContext()
                    + " from=" + (int)dragFrom.x + "," + (int)dragFrom.y
                    + " frames=" + (Time.frameCount - dragStartFrame));
            }
        }

        private static int pointerFrame = -1;
        private static bool loggedFocused;
        private static bool loggedOnSurface;
        private static bool loggedPointerState;
        private static string loggedGeometry;

        /// <summary>
        /// Once per engine frame: the physical mouse presses Unity polled, plus any change in whether
        /// the host window holds focus and the pointer is on the game's surface.
        /// <see cref="Input.GetMouseButtonDown"/> answers from polled device state, so it sees a press
        /// whatever the IMGUI event queue did with it — including one the game never got an event for
        /// because the pointer was outside the window. The focus and surface flags say whether the
        /// mouse was ours to receive at all.
        /// </summary>
        internal static void RecordPointerFrame()
        {
            if (!Active || Time.frameCount == pointerFrame)
            {
                return;
            }
            pointerFrame = Time.frameCount;

            RecordDisplayGeometry();

            bool focused = SystemPointer.HostFocused;
            bool onSurface = PointerSurface.PointerInside;
            if (!loggedPointerState || focused != loggedFocused || onSurface != loggedOnSurface)
            {
                loggedPointerState = true;
                loggedFocused = focused;
                loggedOnSurface = onSurface;
                Record("pointer", "focused=" + focused + " onSurface=" + onSurface + PointerContext());
            }

            for (int button = 0; button < 3; button++)
            {
                if (Input.GetMouseButtonDown(button))
                {
                    Record("press", "button=" + button + PointerContext());
                }
            }
        }

        /// <summary>
        /// The window the game is actually drawing into, logged once at start and again on every
        /// change. The "surface=" figure on each pointer line is <c>UI.screenWidth/Height</c>, the
        /// backbuffer divided by <c>Prefs.UIScale</c>, so a surface that looks wrong is usually a game
        /// window that IS wrong — these are the numbers needed to tell those apart.
        /// </summary>
        private static void RecordDisplayGeometry()
        {
            string geometry = "backbuffer=" + Screen.width + "x" + Screen.height
                + " uiScale=" + Prefs.UIScale
                + " surface=" + UI.screenWidth + "x" + UI.screenHeight
                + " mode=" + Screen.fullScreenMode
                + " display=" + Screen.currentResolution.width + "x" + Screen.currentResolution.height;
            if (geometry == loggedGeometry)
            {
                return;
            }
            loggedGeometry = geometry;
            Record("display", geometry);
        }

        /// <summary>
        /// Where the pointer is and who owns that spot, in the absolute UI points window rects live in.
        /// NOT <c>UI.MousePosUIInvertedUseEventIfCan</c>, which divides the local point by Prefs.UIScale
        /// and unclips in unscaled space (Verse/UI.cs:23-31, :68-71), disagreeing with windowRect above
        /// 100% scale by hundreds of points.
        /// </summary>
        private static string PointerContext()
        {
            Vector2 pos = UI.MousePositionOnUIInverted;
            Window window = Find.WindowStack != null ? Find.WindowStack.GetWindowAt(pos) : null;
            DesignatorManager designators = Current.ProgramState == ProgramState.Playing ? Find.DesignatorManager : null;
            // Unity keeps answering for a pointer another application owns, and the answer is not a
            // position — marked rather than dropped, so a reader can see it is not evidence.
            return " at=" + (int)pos.x + "," + (int)pos.y + (SystemPointer.HostFocused ? string.Empty : "?")
                + " surface=" + UI.screenWidth + "x" + UI.screenHeight
                + " window=" + (window != null ? window.GetType().Name : "none")
                + " top=" + (FocusStack.Top != null ? FocusStack.Top.Name : "none")
                + (designators != null && designators.SelectedDesignator != null
                    ? " designator=" + designators.SelectedDesignator.GetType().Name
                        + " dragging=" + designators.Dragger.Dragging
                    : string.Empty);
        }
    }

    /// <summary>
    /// Skips vanilla's screenshot poll while Alt rides the screenshot key: the recorder's toggle
    /// chord is Alt + that key, and <see cref="KeyBindingDef.JustPressed"/> ignores modifiers, so
    /// without this every toggle would also snap a screenshot and flash its saved-as message.
    /// A prefix skip, not an event eat — the poll reads device state, invisible to Use().
    /// </summary>
    [HarmonyPatch(typeof(ScreenshotTaker), nameof(ScreenshotTaker.Update))]
    public static class ScreenshotTakerAltMaskPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return KeyBindingDefOf.TakeScreenshot == null
                || !(KeyboardHelper.IsAltHeld && KeyBindingDefOf.TakeScreenshot.JustPressed);
        }
    }

    /// <summary>
    /// Shown when a flight recording stops (unless suppressed in settings): the saved path, an
    /// Open folder button riding vanilla's folder vehicle (<see cref="Application.OpenURL"/>, as
    /// Dialog_Options' ShowFolder buttons do), and a suppression checkbox persisted to settings.
    /// Plain captured widgets only — the generic window reader presents it, no bespoke scope.
    /// </summary>
    public sealed class Dialog_RecordingSaved : Window
    {
        private const float ButtonHeight = 35f;
        private const float ButtonSpacing = 10f;

        private readonly string path;
        private bool dontShowAgain;

        public Dialog_RecordingSaved(string path)
        {
            this.path = path;
            // No corner close-X: the OK button already closes, and two identical dismissers
            // read as two different controls.
            closeOnAccept = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            dontShowAgain = settings != null && !settings.ShowRecordingSavedDialog;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(640f, 280f); }
        }

        public override void PreClose()
        {
            base.PreClose();
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && settings.ShowRecordingSavedDialog == dontShowAgain)
            {
                settings.ShowRecordingSavedDialog = !dontShowAgain;
                Mod mod = LoadedModManager.GetMod<RimWorldAccessMod_Settings>();
                if (mod != null)
                {
                    mod.WriteSettings();
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight),
                (string)"RimWorldAccess.FlightRecorder.DialogTitle".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + titleHeight + 8f;
            string body = (string)"RimWorldAccess.FlightRecorder.DialogBody".Translate(path);
            float bodyHeight = Text.CalcHeight(body, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, bodyHeight), body);

            // One bottom strip: the suppression checkbox shares the buttons' band, so the
            // keyboard reads all three as the dialog's button bar.
            float buttonRowY = inRect.yMax - ButtonHeight;
            float checkboxWidth = inRect.width * 0.44f;
            float buttonWidth = (inRect.width - checkboxWidth - ButtonSpacing * 2f) / 2f;
            Widgets.CheckboxLabeled(
                new Rect(inRect.x, buttonRowY + (ButtonHeight - 30f) / 2f, checkboxWidth, 30f),
                (string)"RimWorldAccess.FlightRecorder.DontShowAgain".Translate(), ref dontShowAgain);
            if (Widgets.ButtonText(new Rect(inRect.x + checkboxWidth + ButtonSpacing, buttonRowY, buttonWidth, ButtonHeight),
                (string)"RimWorldAccess.FlightRecorder.OpenFolder".Translate()))
            {
                // A raw path fails silently on macOS (the folder sits under "Application
                // Support", and OpenURL cannot parse the space); a file:// URI opens everywhere.
                Application.OpenURL(new Uri(Path.GetDirectoryName(path)).AbsoluteUri);
            }
            if (Widgets.ButtonText(new Rect(inRect.x + checkboxWidth + buttonWidth + ButtonSpacing * 2f, buttonRowY, buttonWidth, ButtonHeight),
                "OK".Translate()))
            {
                Close();
            }
        }
    }
}
