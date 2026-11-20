using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
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

        /// <summary>Whether the numeric typed-digit buffer has content (scope gate).</summary>
        internal static bool HasDigitBuffer => digitBuffer.Length > 0;

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

        // Key handling lives on SliderDialogScope
        // (src/Shell/Screens/SliderDialogScope.Game.cs) — see its class remarks
        // for the full per-key mapping.

        /// <summary>Escape: cancel (closes dialog without invoking confirmAction).</summary>
        internal static void Cancel()
        {
            currentDialog.Close();
            Close();
            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
        }

        /// <summary>
        /// Backspace: only reachable while the digit buffer has content (the retired
        /// branch's own gate) — shortens the typed buffer and re-applies the remaining
        /// value so the slider tracks what's left in the buffer.
        /// </summary>
        internal static void ProcessBackspace()
        {
            digitBuffer.Length--;
            if (digitBuffer.Length > 0 && int.TryParse(digitBuffer.ToString(), out int remaining))
                SetValueWithSound(remaining);
            else
                TolkHelper.SpeakData((string)"RimWorldAccess.UI.Slider.Cleared".Translate(), SpeechPriority.Low);
        }

        /// <summary>
        /// Digit input applies to the slider live — typing a number moves the slider
        /// (and plays the drag sound) just like dragging it does. Called from the
        /// scope's CharSink on any char.IsDigit character — keypad digits produce the
        /// same chars as the top row, so no separate keycode-range check is needed
        /// (law J-ζ; the retired keycode-range TryReadDigit helper is deleted below,
        /// its sole caller was the just-deleted HandleInput).
        /// </summary>
        internal static void AppendDigit(char digit)
        {
            digitBuffer.Append(digit);
            if (int.TryParse(digitBuffer.ToString(), out int typed))
                SetValueWithSound(typed);
        }

        /// <summary>Up arrow: nudge up by the roundTo step.</summary>
        internal static void StepUp() => Adjust(StepSize());

        /// <summary>Down arrow: nudge down by the roundTo step.</summary>
        internal static void StepDown() => Adjust(-StepSize());

        /// <summary>Right arrow: nudge up by 10% of the range.</summary>
        internal static void PercentUp() => Adjust(PercentStep());

        /// <summary>Left arrow: nudge down by 10% of the range.</summary>
        internal static void PercentDown() => Adjust(-PercentStep());

        /// <summary>Home: jump to the minimum bound.</summary>
        internal static void JumpToMin() => SetValueWithSound(from);

        /// <summary>End: jump to the maximum bound.</summary>
        internal static void JumpToMax() => SetValueWithSound(to);

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
                currentDialog.Close();
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

        /// <summary>
        /// The slider row's datum: this dialog's only control carries
        /// <see cref="ElementRole.Slider"/> plus <see cref="ElementDescription.AtMinimum"/>/
        /// <see cref="ElementDescription.AtMaximum"/> when the value is pinned to a
        /// bound. No separate Value field is set: the resolved text (Label)
        /// already embeds the current number in its own template
        /// (<c>Dialog_Slider.textGetter</c> is an opaque per-value string, not a
        /// static label plus a number vanilla exposes separately), so a second
        /// Value fragment would repeat it.
        ///
        /// <paramref name="includeRange"/> adds the dialog's own from-to bounds as
        /// supplementary info — true when the scope describes the row the cursor has landed
        /// on, false for the value-change announcements below, where the bounds have not
        /// changed and repeating them on every step would be noise.
        /// </summary>
        internal static ElementDescription DescribeValue(bool includeRange)
        {
            int v = GetCurValue();
            var d = new ElementDescription();
            d.Label = ResolveText(v);
            d.Role = ElementRole.Slider;
            d.AtMinimum = v <= from;
            d.AtMaximum = v >= to;
            if (SliderTypeInFocus.BoxesAvailable)
            {
                SliderTypeInText.ApplyBoxValue(d, v);
            }
            if (includeRange)
            {
                d.Extras = "RimWorldAccess.UI.GenericWindow.NumericRange"
                    .Translate(from.ToString(), to.ToString()).ToString();
            }
            return d;
        }

        /// <summary>
        /// Speaks the value a step/jump/typed digit just applied. ComposeFocus (label + role +
        /// state), not ComposeStateChange, is the correct shape for every call, not just the
        /// first: the toggle doctrine's reason to prefer ComposeStateChange on repeat calls
        /// (avoid re-reading a label the user already knows) does not reduce anything here,
        /// since the resolved text itself changes on every step.
        /// </summary>
        private static void AnnounceCurrent()
        {
            TolkHelper.SpeakData(
                AnnouncementComposer.ComposeFocus(
                    DescribeValue(includeRange: false), TranslatedShellVocabulary.Instance, default(ComposeOptions)),
                SpeechPriority.Low);
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

        // RETIRED: TryReadDigit(KeyCode, out char) — its sole
        // caller was the now-deleted HandleInput(Event). Digit routing now arrives
        // character-first through the scope's CharSink (see AppendDigit above).
    }
}
