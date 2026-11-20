using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Edits a focused slider through a mod's type-in box. The owner supplies the
    /// game slider's raw value, which is what the box shows; the buffer reaches the box through
    /// <see cref="SliderTypeInFocus"/>, so the mod parses, clamps and writes the value exactly as it
    /// does for a mouse user's typing. The owner's capture must report the focused slider to
    /// <see cref="SliderTypeInFocus.CursorOnDrawingSlider"/>, or the box never draws.
    /// </summary>
    public sealed class SliderTypeInEditor
    {
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private bool ownsOverride;
        private Action onExit;
        private Action reannounce;

        public bool Editing => session.Editing;

        /// <summary>
        /// Opens the edit on the focused slider, or its range's maximum box when <paramref name="high"/>.
        /// <paramref name="onExit"/> runs as the edit closes; <paramref name="reannounce"/> runs once
        /// the window has redrawn with the new value, and is the only voice after the edit.
        /// </summary>
        public bool TryBegin(float value, bool high, string label, bool announcePrompt, Action onExit, Action reannounce)
        {
            if (!SliderTypeInFocus.BoxesAvailable)
            {
                return false;
            }
            string text = SliderTypeInText.Format(value);
            this.onExit = onExit;
            this.reannounce = reannounce;
            SliderTypeInFocus.BeginOverride(high, text);
            ownsOverride = true;
            session.EnterEdit(text, SliderTypeInText.EditSpec, label, SliderTypeInFocus.SetOverrideText, OnExit, announcePrompt);
            return true;
        }

        /// <summary>Opens the edit from a typed character that can start a number, feeding it in.</summary>
        public bool TryBeginFromChar(char c, float value, bool high, string label, Action onExit, Action reannounce)
        {
            if (!SliderTypeInText.StartsNumber(c) || !TryBegin(value, high, label, announcePrompt: false, onExit, reannounce))
            {
                return false;
            }
            session.FeedChar(c);
            return true;
        }

        /// <summary>Ends any live edit silently (owner popped or torn down).</summary>
        public void Cancel()
        {
            session.CancelIfActive();
            if (ownsOverride)
            {
                ownsOverride = false;
                SliderTypeInFocus.CancelOverride();
            }
            onExit = null;
            reannounce = null;
        }

        private void OnExit()
        {
            Action exit = onExit;
            onExit = null;
            exit?.Invoke();
            SliderTypeInFocus.EndOverride(OnApplied);
        }

        private void OnApplied()
        {
            ownsOverride = false;
            Action announce = reannounce;
            reannounce = null;
            announce?.Invoke();
        }
    }
}
