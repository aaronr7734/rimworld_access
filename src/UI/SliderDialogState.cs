using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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

            // Enter confirms. Typed digits are already applied to the slider live, so a
            // single Enter just invokes the action with the current value.
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                digitBuffer.Clear();
                Confirm();
                return true;
            }

            // Backspace shortens the typed buffer and re-applies the remaining value so
            // the slider tracks what's left in the buffer.
            if (key == KeyCode.Backspace && digitBuffer.Length > 0)
            {
                digitBuffer.Length--;
                if (digitBuffer.Length > 0 && int.TryParse(digitBuffer.ToString(), out int remaining))
                    SetValueWithSound(remaining);
                else
                    TolkHelper.SpeakData((string)"RimWorldAccess.UI.Slider.Cleared".Translate(), SpeechPriority.Low);
                return true;
            }

            // Digit input applies to the slider live — typing a number moves the slider
            // (and plays the drag sound) just like dragging it does.
            if (TryReadDigit(key, out char digit))
            {
                digitBuffer.Append(digit);
                if (int.TryParse(digitBuffer.ToString(), out int typed))
                    SetValueWithSound(typed);
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
                SetValueWithSound(from);
                return true;
            }
            if (key == KeyCode.End)
            {
                SetValueWithSound(to);
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
            SetValueWithSound(GetCurValue() + delta);
        }

        /// <summary>
        /// Clamps and applies a new slider value, announces it, and plays vanilla's
        /// drag-slider sound — but only when the value actually moves, matching
        /// <see cref="Widgets.HorizontalSlider"/>, which stays silent when already pinned to a bound.
        /// </summary>
        private static void SetValueWithSound(int rawValue)
        {
            int oldValue = GetCurValue();
            int newValue = Mathf.Clamp(rawValue, from, to);
            SetCurValue(newValue);
            if (newValue != oldValue)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceCurrent();
        }

        /// <summary>
        /// Applies the current slider value by invoking the dialog's confirm action, then closes.
        /// Idempotent: a no-op once the dialog is already closed, so it is safe to call from both
        /// the keyboard handler and the <c>Window.OnAcceptKeyPressed</c> patch without double-firing.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || currentDialog == null) return;
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
                Log.Error($"[SliderDialogState] Confirm failed: {ex}");
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
