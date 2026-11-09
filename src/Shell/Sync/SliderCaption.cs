using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The one reading of <c>Widgets.HorizontalSlider</c>'s three caption
    /// parameters (decompiled Verse/Widgets.cs:2072), shared by the capture
    /// pass and by the mouse-drag reader so both name and value a slider the
    /// same way.
    ///
    /// Vanilla's own <c>label</c> is a real caption whenever it is present.
    /// The other two slots are ambiguous by construction: mods that draw a
    /// name on the left and a value on the right (VF's Listing_Settings) use
    /// them as a name/value pair, while vanilla's own FloatRange overload
    /// (Verse/Widgets.cs:2065-2070) fills them with the two bounds. Only the
    /// bounds case can be recognised without guessing, and
    /// <see cref="AreBoundMarkers"/> is that test.
    /// </summary>
    /// <summary>A slider's name, and what the reader may do with it (<see cref="SliderCaption.ResolveName"/>).</summary>
    public struct SliderName
    {
        /// <summary>The name, or "" when nothing named the slider.</summary>
        public string Text;

        /// <summary>
        /// Drawn by the very call being read, so a change to it IS the value
        /// moving. A remembered name is one draw pass old and says nothing
        /// about the current value.
        /// </summary>
        public bool Live;

        /// <summary>Remembered names only: the name was drawn as one half of a name/value pair, so the value belongs beside it.</summary>
        public bool CarriesValue;
    }

    public static class SliderCaption
    {
        /// <summary>
        /// True when both side captions are just the slider's own bounds
        /// rendered as text, so neither is a name and neither is the value.
        /// A name/value pair coinciding with both bounds at once is not a
        /// case the parameters can distinguish, and it does not arise.
        /// </summary>
        public static bool AreBoundMarkers(string leftAlignedLabel, string rightAlignedLabel, float min, float max)
        {
            return MarksBound(leftAlignedLabel, min) && MarksBound(rightAlignedLabel, max);
        }

        /// <summary>
        /// Whether a side caption is that bound written out. Vanilla's FloatRange overload passes
        /// the bare number, but a caller is free to group it and append a unit ("1,000 xp" for
        /// 1000), so any rendering the runtime itself produces counts. It must OPEN the caption:
        /// a name that merely mentions a number ("Tier 1 output") is a name, not a bound.
        /// </summary>
        private static bool MarksBound(string caption, float bound)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                return false;
            }
            string text = caption.TrimStart();
            return OpensWith(text, bound.ToString())
                || OpensWith(text, bound.ToString("N0"))
                || OpensWith(text, bound.ToString("F0"));
        }

        /// <summary>The caption opens with that rendering and the number does not continue past it.</summary>
        private static bool OpensWith(string caption, string rendering)
        {
            if (string.IsNullOrEmpty(rendering)
                || !caption.StartsWith(rendering, StringComparison.Ordinal))
            {
                return false;
            }
            if (caption.Length == rendering.Length)
            {
                return true;
            }
            char next = caption[rendering.Length];
            return !char.IsDigit(next) && next != ',' && next != '.';
        }

        /// <summary>The slider's name, or "" when the caller drew none through the widget.</summary>
        public static string EffectiveLabel(string label, string leftAlignedLabel, string rightAlignedLabel, float min, float max)
        {
            // IsNullOrWhiteSpace, not IsNullOrEmpty: Colony Manager Redux's
            // DrawSliderConfig passes label: " " as a spacer with the real
            // name in leftAlignedLabel.
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
            if (!string.IsNullOrWhiteSpace(leftAlignedLabel)
                && !AreBoundMarkers(leftAlignedLabel, rightAlignedLabel, min, max))
            {
                return leftAlignedLabel;
            }
            return "";
        }

        /// <summary>
        /// Whether a caption proves a whole-number-bounded slider fractional: it shows the value
        /// as a percentage of a small range ("Volume: 150%" over 0..2). Ranges above ten units
        /// never qualify; a 0..100 slider captioned "50%" shows its raw value.
        /// </summary>
        public static bool CaptionImpliesFractional(string caption, float value, float min, float max)
        {
            if (string.IsNullOrEmpty(caption) || max - min > 10f
                || (caption.IndexOf('%') < 0 && caption.IndexOf('\uFF05') < 0))
            {
                return false;
            }
            double shown;
            if (!TryReadTrailingNumber(caption, out shown))
            {
                return false;
            }
            return Math.Abs(shown - (double)value * 100.0) <= 1.0;
        }

        /// <summary>"Speed 2.5" at 2.5 gives "Speed"; null unless the whole last word is the
        /// slider's current value.</summary>
        public static string NameBeforeTrailingValue(string caption, float value)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                return null;
            }
            string text = caption.TrimEnd();
            int space = text.LastIndexOf(' ');
            if (space < 1)
            {
                return null;
            }
            double shown;
            if (!TryParseNumberToken(text.Substring(space + 1), out shown)
                || Math.Abs(shown - value) > 0.0001 * Math.Max(1.0, Math.Abs((double)value)))
            {
                return null;
            }
            string name = text.Substring(0, space).TrimEnd();
            return name.Length > 0 ? name : null;
        }

        // A comma before exactly three digits may be thousands grouping, so it is refused.
        private static bool TryParseNumberToken(string token, out double number)
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            const System.Globalization.NumberStyles style = System.Globalization.NumberStyles.AllowLeadingSign
                | System.Globalization.NumberStyles.AllowDecimalPoint;
            if (double.TryParse(token, style, invariant, out number))
            {
                return true;
            }
            int comma = token.IndexOf(',');
            return comma >= 0 && token.Length - comma - 1 != 3 && token.IndexOf('.') < 0
                && double.TryParse(token.Replace(',', '.'), style, invariant, out number);
        }

        /// <summary>The last number in <paramref name="text"/>, read with either decimal separator.</summary>
        private static bool TryReadTrailingNumber(string text, out double number)
        {
            number = 0;
            int end = text.Length - 1;
            while (end >= 0 && !char.IsDigit(text[end]))
            {
                end--;
            }
            if (end < 0)
            {
                return false;
            }
            int start = end;
            while (start > 0 && (char.IsDigit(text[start - 1]) || text[start - 1] == '.' || text[start - 1] == ','))
            {
                start--;
            }
            string token = text.Substring(start, end - start + 1).Replace(',', '.');
            return double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out number);
        }

        /// <summary>
        /// The slider's name across all three places one can come from, in
        /// falling order of authority: the widget's own caption parameters,
        /// the caption the enclosing wrapper drew and discarded before
        /// delegating here (<c>Listing_Standard.SliderLabeled</c> throws its
        /// label away at Verse/Listing_Standard.cs:382), and finally a name
        /// remembered from the previous draw pass for a caller that drew it as
        /// an unrelated widget entirely.
        /// </summary>
        public static SliderName ResolveName(string label, string leftAlignedLabel, string rightAlignedLabel,
            float min, float max, string wrapperLabel, string rememberedLabel, bool rememberedCarriesValue)
        {
            string own = EffectiveLabel(label, leftAlignedLabel, rightAlignedLabel, min, max);
            if (own.Length > 0)
            {
                return new SliderName { Text = own, Live = true };
            }
            if (!string.IsNullOrWhiteSpace(wrapperLabel))
            {
                return new SliderName { Text = wrapperLabel, Live = true };
            }
            if (!string.IsNullOrWhiteSpace(rememberedLabel))
            {
                return new SliderName { Text = rememberedLabel, CarriesValue = rememberedCarriesValue };
            }
            return new SliderName { Text = "" };
        }

        /// <summary>The caller's own rendering of the value, or "" when it drew none.</summary>
        public static string EffectiveValueText(string leftAlignedLabel, string rightAlignedLabel, float min, float max)
        {
            if (!string.IsNullOrWhiteSpace(rightAlignedLabel)
                && !AreBoundMarkers(leftAlignedLabel, rightAlignedLabel, min, max))
            {
                return rightAlignedLabel;
            }
            return "";
        }

        /// <summary>
        /// Fallback rendering for a slider whose caller drew no value text of
        /// its own — the raw number, at the precision it actually carries.
        /// </summary>
        public static string FormatValue(float value)
        {
            bool wholeNumber = Math.Abs(value - Math.Round(value)) < 0.0001;
            return wholeNumber ? value.ToString("F0") : value.ToString("F2");
        }
    }
}
