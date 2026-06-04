using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralizes screen-reader announcements for numeric steppers
    /// (BillConfig repeat/target counts, skill thresholds, fishing-zone
    /// population thresholds, etc.). Owns the ", minimum" / ", maximum"
    /// suffix and the short "Minimum" / "Maximum" boundary phrases so every
    /// stepper emits the same wording.
    ///
    /// The helper is intentionally granular rather than a single SpeakAdjust
    /// method: stepper value formats vary widely (plain ints, "Infinite",
    /// "Unlimited", "50% (12 fish)") and callers keep responsibility for
    /// building the value label.
    /// </summary>
    public static class NumericStepperHelper
    {
        /// <summary>
        /// Announces "Minimum" — the user tried to decrement past the floor
        /// and the value did not change. For the *transition* into the min
        /// value (value changed and now equals min), use
        /// <see cref="SpeakValueAtMinimum"/>.
        /// </summary>
        public static void SpeakAtMinimumBoundary(SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Stepper.Minimum".Loc(), priority);
        }

        /// <summary>
        /// Announces "Maximum" — the user tried to increment past the
        /// ceiling and the value did not change.
        /// </summary>
        public static void SpeakAtMaximumBoundary(SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Stepper.Maximum".Loc(), priority);
        }

        /// <summary>
        /// Directional variant of <see cref="SpeakAtMinimumBoundary"/> /
        /// <see cref="SpeakAtMaximumBoundary"/>: announces "Maximum" when
        /// <paramref name="direction"/> is positive (user pressed increment
        /// and got stuck) and "Minimum" otherwise. Collapses the
        /// <c>direction &gt; 0 ? "Maximum" : "Minimum"</c> pattern that
        /// repeats across every stepper.
        /// </summary>
        public static void SpeakBoundary(int direction, SpeechPriority priority = SpeechPriority.Normal)
        {
            string key = direction > 0
                ? "RimWorldAccess.Stepper.Maximum"
                : "RimWorldAccess.Stepper.Minimum";
            TolkHelper.Speak(key.Loc(), priority);
        }

        /// <summary>
        /// Announces "{valueLabel}, minimum" — used when a stepper adjusts
        /// INTO its floor (value actually changed and is now at the minimum).
        /// Caller provides the formatted value label; the helper owns the
        /// ", minimum" suffix.
        /// </summary>
        public static void SpeakValueAtMinimum(string valueLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Stepper.ValueAtMinimum".Loc(valueLabel), priority);
        }

        /// <summary>
        /// Announces "{valueLabel}, maximum" — used when a stepper adjusts
        /// INTO its ceiling.
        /// </summary>
        public static void SpeakValueAtMaximum(string valueLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Stepper.ValueAtMaximum".Loc(valueLabel), priority);
        }
    }
}
