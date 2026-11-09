using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Type-in boxes a mod draws for a Slider or Range (BetterSliders draws one per slider and
    /// two per range). A compat bracket marks the box's TextField as its owner's companion so
    /// GenericWindowScope folds both into one row, and <see cref="LastValueControlFocused"/>
    /// tells a mod's hover-only box when the keyboard cursor stands where its pointer test looks.
    /// </summary>
    public static partial class WidgetCapture
    {
        private static int sliderTypeInDepth;
        private static int sliderTypeInOwner = -1;
        private static bool sliderTypeInHigh;

        // -1 when the last value control recorded into the suppressed scratch sink, whose
        // indexes mean nothing to a scope.
        private static int lastValueControlIndex = -1;

        // The capture index the owning scope's cursor acts on, which on a fused row differs
        // from focusedIndex: there the ring outlines the caption, not the control.
        private static int focusedControlIndex = -1;

        private static void NoteValueControl(List<CapturedWidget> sink, int index)
        {
            lastValueControlIndex = ReferenceEquals(sink, suppressedScratchSink) ? -1 : index;
        }

        /// <summary>Call right after <see cref="BeginPass"/>.</summary>
        internal static void SetFocusedControl(int index)
        {
            focusedControlIndex = index;
        }

        /// <summary>A live pass is still inside the Slider or Range recorded last, and the owning scope's cursor sits on it.</summary>
        internal static bool LastValueControlFocused
        {
            get
            {
                return passOpen && !detachedPass && lastValueControlIndex >= 0
                    && (lastValueControlIndex == focusedControlIndex || lastValueControlIndex == focusedIndex);
            }
        }

        /// <summary>Start of a mod's type-in box for the Slider or Range recorded last; <paramref name="high"/> marks a range's maximum box.</summary>
        internal static void EnterSliderTypeIn(bool high)
        {
            sliderTypeInDepth++;
            sliderTypeInOwner = lastValueControlIndex;
            sliderTypeInHigh = high;
        }

        internal static void ExitSliderTypeIn()
        {
            if (sliderTypeInDepth > 0)
            {
                sliderTypeInDepth--;
            }
        }

        /// <summary>
        /// Stamps a TextField recorded inside the bracket. RecordTextField checks this ahead of
        /// its range type-in branch, so a box drawn inside FloatRangeWithTypeIn never takes one
        /// of vanilla's own two min/max ordinals.
        /// </summary>
        private static void StampSliderTypeIn(CapturedWidget row)
        {
            row.SliderTypeInOwner = sliderTypeInOwner;
            row.SliderTypeInHigh = sliderTypeInHigh;
        }
    }
}
