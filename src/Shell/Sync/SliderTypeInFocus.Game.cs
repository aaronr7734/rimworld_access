using System;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard side of a mod's slider type-in box (BetterSliders) for every capture, not only the
    /// generic reader's. A capture that knows which drawn slider the keyboard cursor is on reports
    /// it through <see cref="CursorOnDrawingSlider"/>, so the mod shows the box as it would under
    /// the mouse. While a scope edits through the box, the box's text field reports the edit buffer
    /// as typed, so the mod's own parse, clamp and write-back apply.
    /// </summary>
    internal static class SliderTypeInFocus
    {
        // An edit whose box never draws again (its window closed, or a scope whose capture does not
        // report focus) is given up after this many frames rather than holding its callback forever.
        private const int ReleaseTimeoutFrames = 30;

        private static int entryDepth;
        private static bool entryHigh;

        private static bool overrideActive;
        private static bool overrideHigh;
        private static string overrideText;
        private static bool releasePending;
        private static int releaseRequestedFrame = -1;
        private static int appliedFrame = -1;
        private static Action afterApplied;

        /// <summary>Set once the type-in compat's hooks are in place.</summary>
        internal static bool BoxesAvailable { get; set; }

        /// <summary>How the box shows a value it filled in itself; set by the compat that draws it.</summary>
        internal static Func<float, string> BoxValueFormat { get; set; }

        /// <summary>The slider or range drawing right now is the one the keyboard cursor sits on.</summary>
        internal static bool CursorOnDrawingSlider
        {
            get
            {
                return WidgetCapture.LastValueControlFocused || ListingRowCapture.DrawingFocusedSlider
                    || SoleSliderFocus.DrawingFocusedSlider || RangeEditMenuState.DrawingEditedRange
                    || DrugPolicyRowDrawPatch.DrawingFocusedSlider || DefEditorRowCapture.DrawingFocusedSlider;
            }
        }

        /// <summary>Start of the box's own draw; <paramref name="high"/> marks a range's maximum box.</summary>
        internal static void EnterEntry(bool high)
        {
            entryDepth++;
            entryHigh = high;
        }

        internal static void ExitEntry()
        {
            if (entryDepth > 0)
            {
                entryDepth--;
            }
        }

        /// <summary>Starts reporting <paramref name="text"/> as the focused slider's box contents.</summary>
        internal static void BeginOverride(bool high, string text)
        {
            overrideActive = true;
            overrideHigh = high;
            overrideText = text ?? string.Empty;
            releasePending = false;
            appliedFrame = -1;
            afterApplied = null;
        }

        internal static void SetOverrideText(string text)
        {
            overrideText = text ?? string.Empty;
        }

        /// <summary>
        /// Ends the override once one more box draw has delivered the final text, then runs
        /// <paramref name="onApplied"/> after a further full pass, when the owning window has
        /// redrawn with the new value.
        /// </summary>
        internal static void EndOverride(Action onApplied)
        {
            if (!overrideActive)
            {
                onApplied?.Invoke();
                return;
            }
            releasePending = true;
            releaseRequestedFrame = Time.frameCount;
            afterApplied = onApplied;
        }

        internal static void CancelOverride()
        {
            overrideActive = false;
            releasePending = false;
            appliedFrame = -1;
            afterApplied = null;
        }

        /// <summary>
        /// Called for the box's text field with the text it holds. True when the edit buffer should
        /// be reported as the typed result instead.
        /// </summary>
        internal static bool TryOverrideBoxText(string content, out string newContent)
        {
            newContent = null;
            if (!overrideActive || entryDepth == 0 || entryHigh != overrideHigh || !CursorOnDrawingSlider)
            {
                return false;
            }
            string text = overrideText;
            if (releasePending)
            {
                overrideActive = false;
                releasePending = false;
                appliedFrame = Time.frameCount;
            }
            if (string.Equals(text, content, StringComparison.Ordinal))
            {
                return false;
            }
            newContent = text;
            return true;
        }

        /// <summary>Once per GUI pass, from the dispatcher, outside every window's draw.</summary>
        internal static void Tick()
        {
            if (afterApplied == null)
            {
                return;
            }
            if (appliedFrame < 0)
            {
                if (!releasePending || Time.frameCount - releaseRequestedFrame < ReleaseTimeoutFrames)
                {
                    return;
                }
#if DEBUG
                ShellDev.QARecord("capture", "slider type-in edit ended but no box drew to receive it");
#endif
                overrideActive = false;
                releasePending = false;
            }
            else if (Time.frameCount < appliedFrame + 2)
            {
                return;
            }
            Action callback = afterApplied;
            afterApplied = null;
            appliedFrame = -1;
            callback();
        }
    }
}
