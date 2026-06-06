using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard accessibility for vanilla <see cref="Dialog_Slider"/> — the integer-picker
    /// dialog used by "Pick Up Some" (medicine pickup count), drug schedule timers, and
    /// many other "choose a value between A and B" prompts.
    ///
    /// Vanilla Dialog_Slider has only mouse drag-and-confirm. Without keyboard nav, blind
    /// users see a modal pop up with no way to interact, then Escape closes it without
    /// applying the action. This state surfaces the slider's text/range, lets Up/Down
    /// nudge by <c>roundTo</c>, Left/Right by 10% of range, digits + Enter type an exact
    /// value, plain Enter confirms, Escape cancels.
    /// </summary>
    public static class SliderDialogState
    {
        private static readonly FieldInfo TextGetterField =
            AccessTools.Field(typeof(Dialog_Slider), "textGetter");
        private static readonly FieldInfo CurValueField =
            AccessTools.Field(typeof(Dialog_Slider), "curValue");
        private static readonly FieldInfo ConfirmActionField =
            AccessTools.Field(typeof(Dialog_Slider), "confirmAction");

        private static bool isActive;
        private static Dialog_Slider currentDialog;
        private static int from;
        private static int to;
        private static float roundTo;
        private static System.Text.StringBuilder digitBuffer = new System.Text.StringBuilder();

        public static bool IsActive => isActive;

        public static void Open(Dialog_Slider dialog)
        {
            if (dialog == null) return;
            try
            {
                currentDialog = dialog;
                from = dialog.from;
                to = dialog.to;
                roundTo = dialog.roundTo > 0f ? dialog.roundTo : 1f;
                digitBuffer.Clear();
                isActive = true;

                int curValue = GetCurValue();
                string text = ResolveText(curValue);
                TolkHelper.SpeakData(
                    (string)"RimWorldAccess.UI.Slider.OpenPrompt".Translate(text, from.ToString(), to.ToString()),
                    SpeechPriority.Normal);
            }
            catch (Exception ex)
            {
                Log.Error($"[SliderDialogState] Open failed: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            digitBuffer.Clear();
        }

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;
            if (evt.type != EventType.KeyDown) return false;

            var key = evt.keyCode;

            // Escape: cancel (closes dialog without invoking confirmAction).
            if (key == KeyCode.Escape)
            {
                currentDialog.Close(doCloseSound: false);
                Close();
                TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                return true;
            }

            // Enter: if user typed digits, parse and set; otherwise confirm current value.
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (digitBuffer.Length > 0 && int.TryParse(digitBuffer.ToString(), out int typed))
                {
                    int clamped = Mathf.Clamp(typed, from, to);
                    SetCurValue(clamped);
                    digitBuffer.Clear();
                    AnnounceCurrent();
                    return true;
                }
                Confirm();
                return true;
            }

            // Backspace removes from digit buffer.
            if (key == KeyCode.Backspace && digitBuffer.Length > 0)
            {
                digitBuffer.Length--;
                TolkHelper.SpeakData(digitBuffer.Length > 0 ? digitBuffer.ToString() : (string)"RimWorldAccess.UI.Slider.Cleared".Translate(), SpeechPriority.Low);
                return true;
            }

            // Digit input → buffer (announces digit-by-digit so user can verify).
            if (TryReadDigit(key, out char digit))
            {
                digitBuffer.Append(digit);
                TolkHelper.SpeakData(digitBuffer.ToString(), SpeechPriority.Low);
                return true;
            }

            // Up/Down: nudge by roundTo step.
            if (key == KeyCode.UpArrow)
            {
                Adjust(StepSize());
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                Adjust(-StepSize());
                return true;
            }

            // Left/Right: ±10% of range.
            if (key == KeyCode.LeftArrow)
            {
                Adjust(-PercentStep());
                return true;
            }
            if (key == KeyCode.RightArrow)
            {
                Adjust(PercentStep());
                return true;
            }

            // Home/End: jump to bounds.
            if (key == KeyCode.Home)
            {
                SetCurValue(from);
                AnnounceCurrent();
                return true;
            }
            if (key == KeyCode.End)
            {
                SetCurValue(to);
                AnnounceCurrent();
                return true;
            }

            // Modal: consume any other key so it doesn't reach the game underneath.
            return true;
        }

        private static int StepSize()
        {
            return Math.Max(1, Mathf.RoundToInt(roundTo));
        }

        private static int PercentStep()
        {
            int range = to - from;
            return Math.Max(StepSize(), Mathf.RoundToInt(range * 0.1f));
        }

        private static void Adjust(int delta)
        {
            int newValue = Mathf.Clamp(GetCurValue() + delta, from, to);
            SetCurValue(newValue);
            AnnounceCurrent();
        }

        private static void Confirm()
        {
            try
            {
                int curValue = GetCurValue();
                var confirmAction = ConfirmActionField?.GetValue(currentDialog) as Action<int>;
                currentDialog.Close(doCloseSound: false);
                Close();
                confirmAction?.Invoke(curValue);
                TolkHelper.SpeakData((string)"RimWorldAccess.UI.Slider.Confirmed".Translate(curValue.ToString()));
            }
            catch (Exception ex)
            {
                Log.Error($"[SliderDialogState] Confirm failed: {ex.Message}");
                Close();
            }
        }

        private static int GetCurValue()
        {
            try { return (int)(CurValueField?.GetValue(currentDialog) ?? from); }
            catch { return from; }
        }

        private static void SetCurValue(int value)
        {
            try { CurValueField?.SetValue(currentDialog, value); }
            catch (Exception ex) { Log.Warning($"[SliderDialogState] SetCurValue failed: {ex.Message}"); }
        }

        private static void AnnounceCurrent()
        {
            int v = GetCurValue();
            TolkHelper.SpeakData(ResolveText(v), SpeechPriority.Low);
        }

        private static string ResolveText(int value)
        {
            try
            {
                if (TextGetterField?.GetValue(currentDialog) is Func<int, string> getter)
                    return getter(value);
            }
            catch { }
            return value.ToString();
        }

        private static bool TryReadDigit(KeyCode key, out char digit)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                digit = (char)('0' + (key - KeyCode.Alpha0));
                return true;
            }
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                digit = (char)('0' + (key - KeyCode.Keypad0));
                return true;
            }
            digit = '\0';
            return false;
        }
    }
}
