using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One editing session: owns the working text buffer, the field's spec, and
    /// confirm/cancel callbacks. Routes character/backspace/copy/paste/enter/escape
    /// events from the priority -1.5 dispatch in <see cref="UnifiedKeyboardPatch"/>.
    /// Modal — only one controller is active at a time via <see cref="TextInputManager"/>.
    ///
    /// Supports a text-review cursor: Left/Right move one char, Ctrl+Left/Right move one
    /// word, Home/End jump, Shift+{arrows,Home,End} extend selection, Delete removes
    /// forward, Up/Down re-read the whole buffer. Typing still announces each character
    /// (chatty mode) — review is purely additive.
    /// </summary>
    public sealed class TextInputController
    {
        private string currentText = string.Empty;
        private bool replaceOnFirstKeystroke;
        private int cursorPos;
        private int selectionAnchor;

        public TextFieldSpec Spec { get; private set; }
        public string CurrentText => currentText;
        public bool IsEmpty => string.IsNullOrEmpty(currentText);
        public int CursorPos => cursorPos;
        public bool HasSelection => cursorPos != selectionAnchor;

        private Action<string> onConfirm;
        private Action onCancel;

        /// <summary>
        /// Begin an editing session. When <paramref name="modal"/> is true (default), the
        /// controller registers as <see cref="TextInputManager.Active"/> so UnifiedKeyboardPatch
        /// routes ALL keys to it (modal text edit — used by renames). When false, the
        /// caller (an embedded text field inside a larger menu) is responsible for routing
        /// keys to <see cref="HandleCharacter"/> / <see cref="HandleEnter"/> / etc. itself.
        /// When <paramref name="replaceOnType"/> is true and initial text is non-empty,
        /// the first character/paste replaces the existing text.
        /// </summary>
        public void Begin(
            string initialText,
            TextFieldSpec spec,
            Action<string> onConfirm,
            Action onCancel = null,
            bool replaceOnType = true,
            bool modal = true)
        {
            currentText = initialText ?? string.Empty;
            replaceOnFirstKeystroke = replaceOnType && !string.IsNullOrEmpty(currentText);
            cursorPos = currentText.Length;
            selectionAnchor = cursorPos;
            Spec = spec;
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            if (modal) TextInputManager.SetActive(this);

            string label = spec?.LabelKey != null ? spec.LabelKey.Translate().ToString() : string.Empty;
            string preview = string.IsNullOrEmpty(currentText)
                ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                : currentText;
            string announceKey = spec != null && spec.MultiLine
                ? "RimWorldAccess.TextInput.EditingMultiLineField"
                : "RimWorldAccess.TextInput.EditingField";
            TolkHelper.Speak(announceKey.Loc(label, preview), SpeechPriority.High);
        }

        public void HandleCharacter(char c)
        {
            if (c == '\n')
            {
                // Newlines only allowed in multi-line fields.
                if (Spec == null || !Spec.MultiLine) return;
            }
            else if (char.IsControl(c)) return;

            if (replaceOnFirstKeystroke)
            {
                currentText = string.Empty;
                cursorPos = 0;
                selectionAnchor = 0;
                replaceOnFirstKeystroke = false;
            }
            if (HasSelection) DeleteSelectionInternal();
            currentText = currentText.Insert(cursorPos, c.ToString());
            cursorPos++;
            selectionAnchor = cursorPos;
            if (c == '\n')
                TolkHelper.Speak("RimWorldAccess.TextInput.NewLine".Loc(), SpeechPriority.High);
            else
                TolkHelper.SpeakData(c.ToString(), SpeechPriority.High);
        }

        public void HandleBackspace()
        {
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                string removed = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
                return;
            }
            if (cursorPos == 0) return;
            char c = currentText[cursorPos - 1];
            currentText = currentText.Remove(cursorPos - 1, 1);
            cursorPos--;
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(c), SpeechPriority.High);
        }

        public void HandleDelete()
        {
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                string removed = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
                return;
            }
            if (cursorPos >= currentText.Length) return;
            char c = currentText[cursorPos];
            currentText = currentText.Remove(cursorPos, 1);
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(c), SpeechPriority.High);
        }

        public void HandleArrowLeft(bool shift, bool ctrl)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            int newCursor;
            if (ctrl)
                newCursor = FindPreviousWordBoundary(cursorPos);
            else if (HasSelection && !shift)
                newCursor = Math.Min(cursorPos, selectionAnchor);
            else
                newCursor = Math.Max(0, cursorPos - 1);

            cursorPos = newCursor;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl, leftward: true);
        }

        public void HandleArrowRight(bool shift, bool ctrl)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            int newCursor;
            if (ctrl)
                newCursor = FindNextWordBoundary(cursorPos);
            else if (HasSelection && !shift)
                newCursor = Math.Max(cursorPos, selectionAnchor);
            else
                newCursor = Math.Min(currentText.Length, cursorPos + 1);

            cursorPos = newCursor;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl, leftward: false);
        }

        /// <summary>
        /// Home: in single-line mode (or with Ctrl), jumps to the start of the field.
        /// In multi-line mode without Ctrl, jumps to the start of the current line.
        /// </summary>
        public void HandleHome(bool shift, bool ctrl = false)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            if (Spec != null && Spec.MultiLine && !ctrl)
                cursorPos = FindStartOfCurrentLine(cursorPos);
            else
                cursorPos = 0;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl: false, leftward: true);
        }

        /// <summary>
        /// End: in single-line mode (or with Ctrl), jumps to the end of the field.
        /// In multi-line mode without Ctrl, jumps to the end of the current line.
        /// </summary>
        public void HandleEnd(bool shift, bool ctrl = false)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            if (Spec != null && Spec.MultiLine && !ctrl)
                cursorPos = FindEndOfCurrentLine(cursorPos);
            else
                cursorPos = currentText.Length;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl: false, leftward: false);
        }

        /// <summary>
        /// Multi-line only: move cursor up one line, preserving column position.
        /// If already on the first line, stays at line start and announces the first line.
        /// </summary>
        public void HandleArrowUp(bool shift)
        {
            if (Spec == null || !Spec.MultiLine)
            {
                ReadCurrentText();
                return;
            }
            replaceOnFirstKeystroke = false;
            int currentLineStart = FindStartOfCurrentLine(cursorPos);
            if (currentLineStart == 0)
            {
                // Already on first line — snap to start and announce.
                cursorPos = 0;
                if (!shift) selectionAnchor = cursorPos;
                AnnounceLine(0);
                return;
            }
            int col = cursorPos - currentLineStart;
            int prevLineStart = FindStartOfCurrentLine(currentLineStart - 1);
            int prevLineLen = (currentLineStart - 1) - prevLineStart; // excludes the \n
            int targetCol = Math.Min(col, prevLineLen);
            cursorPos = prevLineStart + targetCol;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceLine(prevLineStart);
        }

        /// <summary>
        /// Multi-line only: move cursor down one line, preserving column position.
        /// If already on the last line, snaps to the end of the field and announces it.
        /// </summary>
        public void HandleArrowDown(bool shift)
        {
            if (Spec == null || !Spec.MultiLine)
            {
                ReadCurrentText();
                return;
            }
            replaceOnFirstKeystroke = false;
            int currentLineStart = FindStartOfCurrentLine(cursorPos);
            int currentLineEnd = FindEndOfCurrentLine(cursorPos);
            if (currentLineEnd >= currentText.Length)
            {
                // Already on last line — snap to end and announce.
                cursorPos = currentText.Length;
                if (!shift) selectionAnchor = cursorPos;
                AnnounceLine(currentLineStart);
                return;
            }
            int col = cursorPos - currentLineStart;
            int nextLineStart = currentLineEnd + 1;
            int nextLineEnd = FindEndOfCurrentLine(nextLineStart);
            int nextLineLen = nextLineEnd - nextLineStart;
            int targetCol = Math.Min(col, nextLineLen);
            cursorPos = nextLineStart + targetCol;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceLine(nextLineStart);
        }

        public void HandleCopy()
        {
            string toCopy = HasSelection ? GetSelectedText() : (currentText ?? string.Empty);
            GUIUtility.systemCopyBuffer = toCopy;
            TolkHelper.Speak("RimWorldAccess.TextInput.Copied".Loc(), SpeechPriority.High);
        }

        /// <summary>
        /// Cut: with a selection, copies selection to clipboard and removes it. Without a
        /// selection, cuts the entire field (matches VS Code's "cut line" convention applied
        /// to a single-line field — useful for "clear and save to clipboard").
        /// </summary>
        public void HandleCut()
        {
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                GUIUtility.systemCopyBuffer = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Cut".Loc(), SpeechPriority.High);
                return;
            }
            if (currentText.Length == 0) return;
            GUIUtility.systemCopyBuffer = currentText;
            currentText = string.Empty;
            cursorPos = 0;
            selectionAnchor = 0;
            TolkHelper.Speak("RimWorldAccess.TextInput.Cut".Loc(), SpeechPriority.High);
        }

        /// <summary>
        /// Ctrl+Backspace: delete the word to the left of the cursor. With a selection,
        /// falls back to deleting the selection (same as plain Backspace).
        /// </summary>
        public void HandleBackspaceWord()
        {
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                HandleBackspace();
                return;
            }
            if (cursorPos == 0) return;
            int prev = FindPreviousWordBoundary(cursorPos);
            if (prev >= cursorPos) return;
            string removed = currentText.Substring(prev, cursorPos - prev);
            currentText = currentText.Remove(prev, cursorPos - prev);
            cursorPos = prev;
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
        }

        /// <summary>
        /// Ctrl+Delete: delete the word to the right of the cursor. With a selection,
        /// falls back to deleting the selection (same as plain Delete).
        /// </summary>
        public void HandleDeleteWord()
        {
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                HandleDelete();
                return;
            }
            if (cursorPos >= currentText.Length) return;
            int next = FindNextWordBoundary(cursorPos);
            if (next <= cursorPos) return;
            string removed = currentText.Substring(cursorPos, next - cursorPos);
            currentText = currentText.Remove(cursorPos, next - cursorPos);
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
        }

        public void HandleSelectAll()
        {
            replaceOnFirstKeystroke = false;
            if (currentText.Length == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.Empty".Loc(), SpeechPriority.High);
                return;
            }
            selectionAnchor = 0;
            cursorPos = currentText.Length;
            // For large buffers (e.g. a scenario description), reading the whole thing
            // would take a long time; announce the char count instead. The user can
            // press Up/Down to re-read the full contents explicitly.
            const int LongTextThreshold = 200;
            if (currentText.Length > LongTextThreshold)
                TolkHelper.Speak("RimWorldAccess.TextInput.SelectedAllLong".Loc(currentText.Length), SpeechPriority.High);
            else
                TolkHelper.Speak("RimWorldAccess.TextInput.SelectedAll".Loc(currentText), SpeechPriority.High);
        }

        public void HandlePaste()
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboard))
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.ClipboardEmpty".Loc(), SpeechPriority.High);
                return;
            }

            string candidate;
            int insertionEnd;
            if (replaceOnFirstKeystroke)
            {
                candidate = clipboard;
                insertionEnd = clipboard.Length;
            }
            else if (HasSelection)
            {
                int lo = Math.Min(cursorPos, selectionAnchor);
                int hi = Math.Max(cursorPos, selectionAnchor);
                candidate = currentText.Substring(0, lo) + clipboard + currentText.Substring(hi);
                insertionEnd = lo + clipboard.Length;
            }
            else
            {
                candidate = currentText.Substring(0, cursorPos) + clipboard + currentText.Substring(cursorPos);
                insertionEnd = cursorPos + clipboard.Length;
            }

            var result = TextFieldValidator.Validate(candidate, Spec);
            if (!result.IsOk)
            {
                TolkHelper.SpeakData(TextFieldValidator.AnnounceRejection(result, Spec), SpeechPriority.High);
                return;
            }

            currentText = candidate;
            cursorPos = insertionEnd;
            selectionAnchor = cursorPos;
            replaceOnFirstKeystroke = false;
            TolkHelper.SpeakData(SummarizePaste(clipboard), SpeechPriority.High);
        }

        public void HandleEnter()
        {
            var result = TextFieldValidator.Validate(currentText, Spec);
            if (!result.IsOk)
            {
                TolkHelper.SpeakData(TextFieldValidator.AnnounceRejection(result, Spec), SpeechPriority.High);
                return;
            }
            var cb = onConfirm;
            string text = currentText;
            Close();
            cb?.Invoke(text);
        }

        public void HandleEscape()
        {
            var cb = onCancel;
            Close();
            cb?.Invoke();
        }

        public void Cancel() => HandleEscape();

        public void ReadCurrentText()
        {
            if (string.IsNullOrEmpty(currentText))
                TolkHelper.Speak("RimWorldAccess.TextInput.Empty".Loc());
            else
                TolkHelper.SpeakData(currentText);
        }

        /// <summary>
        /// Cursor-review subset of <see cref="HandleEvent"/>: handles only Left/Right/Home/End/Delete
        /// (with Shift / Ctrl modifiers). Intended for embedded sites whose surrounding list still
        /// owns Up/Down and Enter/Escape — call this from the site's input handler before its own
        /// arrow-nav branch so a user editing a field gets cursor review without losing list nav.
        /// </summary>
        public bool HandleCursorNavEvent(Event evt)
        {
            if (evt.type != EventType.KeyDown) return false;
            bool shift = evt.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: HandleArrowLeft(shift, ctrl); return true;
                case KeyCode.RightArrow: HandleArrowRight(shift, ctrl); return true;
                case KeyCode.Home: HandleHome(shift, ctrl); return true;
                case KeyCode.End: HandleEnd(shift, ctrl); return true;
                case KeyCode.Delete:
                    if (ctrl) HandleDeleteWord();
                    else HandleDelete();
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Dispatch an IMGUI key event to the appropriate handler. Returns true if the
        /// event was consumed and the caller should call <c>Event.current.Use()</c>.
        /// </summary>
        public bool HandleEvent(Event evt)
        {
            if (evt.type != EventType.KeyDown) return false;

            bool shift = evt.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;

            // Ctrl-modified non-cursor shortcuts.
            if (ctrl && !shift)
            {
                if (evt.keyCode == KeyCode.C) { HandleCopy(); return true; }
                if (evt.keyCode == KeyCode.V) { HandlePaste(); return true; }
                if (evt.keyCode == KeyCode.X) { HandleCut(); return true; }
                if (evt.keyCode == KeyCode.A) { HandleSelectAll(); return true; }
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Shift+Enter inserts a newline in multi-line fields; plain Enter always confirms.
                    if (shift && Spec != null && Spec.MultiLine)
                    {
                        HandleCharacter('\n');
                        return true;
                    }
                    HandleEnter();
                    return true;
                case KeyCode.Escape:
                    HandleEscape();
                    return true;
                case KeyCode.Backspace:
                    if (ctrl) HandleBackspaceWord();
                    else HandleBackspace();
                    return true;
                case KeyCode.Delete:
                    if (ctrl) HandleDeleteWord();
                    else HandleDelete();
                    return true;
                case KeyCode.LeftArrow:
                    HandleArrowLeft(shift, ctrl);
                    return true;
                case KeyCode.RightArrow:
                    HandleArrowRight(shift, ctrl);
                    return true;
                case KeyCode.Home:
                    HandleHome(shift, ctrl);
                    return true;
                case KeyCode.End:
                    HandleEnd(shift, ctrl);
                    return true;
                case KeyCode.UpArrow:
                    // Multi-line: navigate lines. Single-line OR Ctrl-held: re-read whole field.
                    if (Spec != null && Spec.MultiLine && !ctrl) HandleArrowUp(shift);
                    else ReadCurrentText();
                    return true;
                case KeyCode.DownArrow:
                    if (Spec != null && Spec.MultiLine && !ctrl) HandleArrowDown(shift);
                    else ReadCurrentText();
                    return true;
            }

            // Layout-aware character (keyCode == None, character != '\0'). Skip control
            // chars (e.g. \b backspace) — those are handled above by KeyCode.Backspace.
            if (evt.keyCode == KeyCode.None && evt.character != '\0' && !char.IsControl(evt.character))
            {
                HandleCharacter(evt.character);
                return true;
            }

            return false;
        }

        private void Close()
        {
            currentText = string.Empty;
            cursorPos = 0;
            selectionAnchor = 0;
            replaceOnFirstKeystroke = false;
            Spec = null;
            onConfirm = null;
            onCancel = null;
            if (TextInputManager.Active == this)
                TextInputManager.Clear();
        }

        private string GetSelectedText()
        {
            int lo = Math.Min(cursorPos, selectionAnchor);
            int hi = Math.Max(cursorPos, selectionAnchor);
            return currentText.Substring(lo, hi - lo);
        }

        private void DeleteSelectionInternal()
        {
            int lo = Math.Min(cursorPos, selectionAnchor);
            int hi = Math.Max(cursorPos, selectionAnchor);
            currentText = currentText.Remove(lo, hi - lo);
            cursorPos = lo;
            selectionAnchor = lo;
        }

        private int FindStartOfCurrentLine(int pos)
        {
            if (pos <= 0) return 0;
            int i = Math.Min(pos, currentText.Length) - 1;
            while (i >= 0 && currentText[i] != '\n') i--;
            return i + 1;
        }

        private int FindEndOfCurrentLine(int pos)
        {
            int i = Math.Max(0, Math.Min(pos, currentText.Length));
            while (i < currentText.Length && currentText[i] != '\n') i++;
            return i;
        }

        private void AnnounceLine(int lineStart)
        {
            int lineEnd = FindEndOfCurrentLine(lineStart);
            if (lineStart >= lineEnd)
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.BlankLine".Loc(), SpeechPriority.High);
                return;
            }
            TolkHelper.SpeakData(currentText.Substring(lineStart, lineEnd - lineStart), SpeechPriority.High);
        }

        private string GetWordAt(int pos)
        {
            if (pos < 0 || pos >= currentText.Length) return string.Empty;
            if (!char.IsLetterOrDigit(currentText[pos])) return string.Empty;
            int end = pos;
            while (end < currentText.Length && char.IsLetterOrDigit(currentText[end])) end++;
            return currentText.Substring(pos, end - pos);
        }

        private int FindNextWordBoundary(int from)
        {
            int i = from;
            while (i < currentText.Length && char.IsLetterOrDigit(currentText[i])) i++;
            while (i < currentText.Length && !char.IsLetterOrDigit(currentText[i])) i++;
            return i;
        }

        private int FindPreviousWordBoundary(int from)
        {
            int i = from;
            while (i > 0 && !char.IsLetterOrDigit(currentText[i - 1])) i--;
            while (i > 0 && char.IsLetterOrDigit(currentText[i - 1])) i--;
            return i;
        }

        private void AnnounceCursorMove(int oldCursor, int newCursor, bool shift, bool ctrl, bool leftward)
        {
            if (currentText.Length == 0) return; // nothing to announce on an empty field

            // Shift-selection: announce the range we just added/removed. If no movement
            // (hit a boundary), repeat the first or last char based on direction.
            if (shift)
            {
                int lo = Math.Min(oldCursor, newCursor);
                int hi = Math.Max(oldCursor, newCursor);
                if (lo < hi)
                {
                    TolkHelper.SpeakData(currentText.Substring(lo, hi - lo), SpeechPriority.High);
                    return;
                }
                int edge = leftward ? 0 : currentText.Length - 1;
                TolkHelper.SpeakData(currentText[edge].ToString(), SpeechPriority.High);
                return;
            }

            // Ctrl+arrow (word jump): announce the word at the new cursor position. If no
            // word there (boundary hit), announce the first/last word based on direction,
            // falling back to the edge char when the field has no words at all.
            if (ctrl)
            {
                string wordAtCursor = GetWordAt(newCursor);
                if (!string.IsNullOrEmpty(wordAtCursor))
                {
                    TolkHelper.SpeakData(wordAtCursor, SpeechPriority.High);
                    return;
                }
                string edgeWord = leftward ? GetFirstWord() : GetLastWord();
                if (!string.IsNullOrEmpty(edgeWord))
                {
                    TolkHelper.SpeakData(edgeWord, SpeechPriority.High);
                    return;
                }
                int edge = leftward ? 0 : currentText.Length - 1;
                TolkHelper.SpeakData(currentText[edge].ToString(), SpeechPriority.High);
                return;
            }

            // Plain arrow / Home / End: announce the char at the cursor. If the cursor
            // is past the last char (at `length`) or didn't move off a boundary, repeat
            // the edge char based on direction.
            int idx = newCursor < currentText.Length ? newCursor : currentText.Length - 1;
            TolkHelper.SpeakData(currentText[idx].ToString(), SpeechPriority.High);
        }

        private string GetFirstWord()
        {
            int i = 0;
            while (i < currentText.Length && !char.IsLetterOrDigit(currentText[i])) i++;
            if (i >= currentText.Length) return string.Empty;
            int start = i;
            while (i < currentText.Length && char.IsLetterOrDigit(currentText[i])) i++;
            return currentText.Substring(start, i - start);
        }

        private string GetLastWord()
        {
            int i = currentText.Length - 1;
            while (i >= 0 && !char.IsLetterOrDigit(currentText[i])) i--;
            if (i < 0) return string.Empty;
            int end = i + 1;
            while (i >= 0 && char.IsLetterOrDigit(currentText[i])) i--;
            return currentText.Substring(i + 1, end - i - 1);
        }

        private static string SummarizePaste(string clipboard)
        {
            int lineCount = 1;
            int wordCount = 0;
            bool inWord = false;
            for (int i = 0; i < clipboard.Length; i++)
            {
                char c = clipboard[i];
                if (c == '\n') lineCount++;
                bool isWs = char.IsWhiteSpace(c);
                if (!isWs && !inWord) { wordCount++; inWord = true; }
                else if (isWs) inWord = false;
            }

            if (lineCount > 1)
                return "RimWorldAccess.TextInput.PastedLines".Translate(lineCount, wordCount);
            if (wordCount > 5)
                return "RimWorldAccess.TextInput.PastedWords".Translate(wordCount);
            return "RimWorldAccess.TextInput.PastedShort".Translate(clipboard);
        }
    }
}
