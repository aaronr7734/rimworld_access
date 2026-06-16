using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// IME (Input Method Editor) composition funnel for CJK languages (Simplified/Traditional
    /// Chinese, Japanese, Korean).
    ///
    /// The mod captures all text input synthetically — it reads <c>Event.current.character</c>
    /// and appends to its own buffers (see <see cref="TextInputController"/>,
    /// <see cref="TypeaheadDispatcher"/>). That works for direct keyboard layouts (one keystroke
    /// = one finished character, e.g. Latin or Cyrillic), but it cannot work for composition-based
    /// IME input: pinyin keystrokes have to be composed through an OS candidate window into a real,
    /// focused Unity <c>GUI.TextField</c> before the finished character exists. With no such field,
    /// the committed Chinese characters have nowhere to land and only raw Latin letters get through.
    ///
    /// This host bridges that gap. While a text sink is active AND the active game language is a
    /// CJK/IME language, it draws an offscreen, focused <c>GUI.TextField</c> every OnGUI pass and
    /// turns on <c>Input.imeCompositionMode</c>. Unity routes IME composition into that hidden
    /// field; we harvest the committed characters by diffing the field's returned string and feed
    /// them back through the mod's normal character-routing path. The hidden field is purely a
    /// "commit catcher" — we never use its cursor, selection, or editing; the authoritative buffer
    /// stays in the mod's own controllers.
    ///
    /// Routing rule (see <see cref="TryRouteKeyDown"/>):
    ///   - While composing (a candidate window is open): every key belongs to the IME — it
    ///     navigates candidates, commits, or edits the in-progress pinyin. Route to the field.
    ///   - While NOT composing: only letter keys begin/continue composition, so only those route
    ///     to the field. Everything else (digits, space, punctuation, arrows, Enter, hotkeys)
    ///     keeps its normal path, so cursor review, list navigation, and submit behave exactly as
    ///     they do for non-IME players.
    ///
    /// Gated to CJK languages so direct-layout players (the overwhelming majority) keep their exact
    /// current code path untouched — no hidden field, no focus management, zero regression surface.
    /// </summary>
    public static class ImeInputHost
    {
        // Control name + offscreen rect for the hidden funnel field. The field must actually be
        // drawn (IMGUI is immediate-mode; an undrawn control processes no events) but never needs
        // to be visible — a screen reader user perceives it only through the OS IME candidate
        // window, which the player's screen reader reads at the OS level.
        private const string FieldName = "RWA_IME_FunnelField";
        private static readonly Rect OffscreenRect = new Rect(-400f, -400f, 200f, 30f);

        private static bool active;
        // Seed string passed into the field. We reset it to empty whenever no composition is in
        // progress so committed text always appears as a fresh append we can diff out, and the
        // buffer never grows unbounded. While composing we leave it (the commit is still pending).
        private static string fieldValue = string.Empty;
        private static bool composingLastDraw;
        private static IMECompositionMode savedMode;
        private static bool hasSavedMode;

        /// <summary>True while the funnel is engaged (a text sink is active in a CJK language).</summary>
        public static bool IsActive => active;

        /// <summary>
        /// True if an IME composition was in progress as of the most recent field draw. Callers
        /// use this to decide whether a key belongs to the IME (composing) or to the mod's own
        /// handlers (not composing). See the routing rule in the class summary.
        /// </summary>
        public static bool IsComposing => composingLastDraw;

        /// <summary>
        /// Called once per OnGUI pass from the top of the keyboard prefix. Manages the active
        /// state (toggling <c>Input.imeCompositionMode</c> on the edge) and, on NON-KeyDown passes,
        /// draws the hidden field so it keeps keyboard focus and its composition state alive between
        /// keystrokes. KeyDown passes draw via <see cref="TryRouteKeyDown"/> instead, so the field
        /// is drawn exactly once per pass (drawing it twice in one pass is an IMGUI error).
        /// </summary>
        public static void Pump(bool sinkActive, Action<char> onCommitted)
        {
            bool shouldBeActive = sinkActive && LanguageUsesIme();
            if (shouldBeActive && !active) Activate();
            else if (!shouldBeActive && active) Deactivate();

            if (!active) return;

            // Draw ONLY on the layout and repaint passes — never on a KeyDown (TryRouteKeyDown draws
            // and routes those) and never on an already-consumed event. Drawing the field twice in a
            // single OnGUI pass is an IMGUI error, which would otherwise happen when another prefix
            // (e.g. WindowlessDialogInputPatch at VeryHigh) consumes a KeyDown — turning its type to
            // Used — before this Pump runs in the same pass.
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint)
                DrawAndHarvest(onCommitted);
        }

        /// <summary>
        /// On a KeyDown pass, decide whether this key belongs to the IME and, if so, draw the hidden
        /// field to let Unity process it, harvest any committed characters, and return true (the
        /// caller should then <c>Event.current.Use()</c> and return). Returns false when the key
        /// should follow its normal path (the caller proceeds with its usual handling).
        /// </summary>
        public static bool TryRouteKeyDown(Event evt, Action<char> onCommitted)
        {
            if (!active || evt.type != EventType.KeyDown) return false;

            bool routeToField;
            if (composingLastDraw)
            {
                // Mid-composition: candidate navigation, commit (Enter/Space), and pinyin editing
                // (Backspace/arrows) all belong to the IME.
                routeToField = true;
            }
            else
            {
                // Not composing: only a letter starts or continues composition. A letter reaches us
                // either as a letter KeyCode (A-Z) or as the keyCode==None character twin Unity fires
                // for printable input. A held Ctrl/Alt means it's a shortcut, never composition text.
                bool modified = KeyboardHelper.IsAltHeld || KeyboardHelper.IsCtrlHeld;
                bool isLetterKeyCode = evt.keyCode >= KeyCode.A && evt.keyCode <= KeyCode.Z;
                bool isLetterChar = evt.keyCode == KeyCode.None && evt.character != '\0'
                                    && char.IsLetter(evt.character);
                routeToField = !modified && (isLetterKeyCode || isLetterChar);
            }

            if (!routeToField) return false;

            DrawAndHarvest(onCommitted);
            return true;
        }

        /// <summary>
        /// Draw the offscreen field, force focus to it, and feed any newly-committed characters to
        /// <paramref name="onCommitted"/>. Updates <see cref="IsComposing"/> from the live IME
        /// composition string. Must be called inside an OnGUI context.
        /// </summary>
        public static void DrawAndHarvest(Action<char> onCommitted)
        {
            if (!active) return;

            // Place the OS candidate window at a sane on-screen point. It is irrelevant to a screen
            // reader user visually, but some platforms misbehave if the composition cursor is off-screen.
            Input.compositionCursorPos = new Vector2(100f, 100f);

            GUI.SetNextControlName(FieldName);
            string newValue = GUI.TextField(OffscreenRect, fieldValue);

            // Keep the hidden field focused so the OS IME always has a target to compose into.
            if (GUI.GetNameOfFocusedControl() != FieldName)
                GUI.FocusControl(FieldName);

            // Committed text appears appended to the field's value (its cursor is always at the end,
            // since we never move it and reset to empty between commits). The appended slice is the
            // characters the IME just committed this pass.
            if (newValue.Length > fieldValue.Length)
            {
                string committed = newValue.Substring(fieldValue.Length);
                for (int i = 0; i < committed.Length; i++)
                {
                    char c = committed[i];
                    if (!char.IsControl(c))
                        onCommitted?.Invoke(c);
                }
            }

            composingLastDraw = !string.IsNullOrEmpty(Input.compositionString);
            // Reset between commits so the next commit reads as a fresh append; keep the value while
            // composing so we don't disturb the in-progress composition.
            fieldValue = composingLastDraw ? newValue : string.Empty;
        }

        private static void Activate()
        {
            active = true;
            fieldValue = string.Empty;
            composingLastDraw = false;
            savedMode = Input.imeCompositionMode;
            hasSavedMode = true;
            Input.imeCompositionMode = IMECompositionMode.On;
        }

        private static void Deactivate()
        {
            active = false;
            fieldValue = string.Empty;
            composingLastDraw = false;
            Input.imeCompositionMode = hasSavedMode ? savedMode : IMECompositionMode.Auto;
            hasSavedMode = false;
            if (GUI.GetNameOfFocusedControl() == FieldName)
                GUI.FocusControl(null);
        }

        /// <summary>
        /// True when the active game language is composition-based (CJK), where direct
        /// <c>Event.character</c> capture cannot work and the IME funnel is required. Matched on the
        /// language's folder name (both the current and legacy ASCII names) so it holds regardless of
        /// whether the folder uses the native or legacy form.
        /// </summary>
        internal static bool LanguageUsesIme()
        {
            LoadedLanguage lang = LanguageDatabase.activeLanguage;
            if (lang == null) return false;
            string name = ((lang.folderName ?? string.Empty) + "|" + (lang.LegacyFolderName ?? string.Empty))
                .ToLowerInvariant();
            return name.Contains("chinese") || name.Contains("japanese") || name.Contains("korean");
        }
    }
}
