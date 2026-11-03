using System;
using System.Globalization;

namespace RimWorldAccess.Shell
{
    /// <summary>A captured row's screen rect, described without any GUI types.</summary>
    public struct LineBox
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public float XMax => X + Width;
        public float YMax => Y + Height;
        public float CenterY => Y + Height / 2f;
    }

    /// <summary>
    /// A slider whose own caption is nothing but its value ("3", "40%"), drawn to the right of a
    /// plain Label naming it on the same line, optionally with an icon of the same thing between
    /// the two. The generic reader folds such a row into one slider named by the Label, only on
    /// strict draw adjacency within one clip: it runs on every unregistered mod window, where a
    /// wrong caption is worse than none.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class ValueLabeledSlider
    {
        private const float EdgeSlack = 2f;
        private const int MaxSuffixLength = 6;

        /// <summary>
        /// True when <paramref name="label"/> is a number, optionally followed by a short unit or
        /// percent sign, that renders <paramref name="value"/> at the precision it shows (a percent
        /// may also render value × 100). A number that does not match the value is a name.
        /// </summary>
        public static bool LabelIsValue(string label, float value)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }
            string text = label.Trim();
            int end = 0;
            if (text[0] == '+' || text[0] == '-')
            {
                end = 1;
            }
            int digitsStart = end;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ','))
            {
                end++;
            }
            string number = text.Substring(0, end);
            if (end == digitsStart || !char.IsDigit(text[digitsStart]) || !char.IsDigit(number[number.Length - 1]))
            {
                return false;
            }
            string suffix = text.Substring(end).Trim();
            if (suffix.Length > MaxSuffixLength)
            {
                return false;
            }
            foreach (char c in suffix)
            {
                if (char.IsDigit(c) || char.IsWhiteSpace(c))
                {
                    return false;
                }
            }
            bool percent = suffix.IndexOf('%') >= 0 || suffix.IndexOf('％') >= 0;
            // Either separator may be the decimal point, depending on the language.
            return Renders(number, ',', '.', value, percent) || Renders(number, '.', ',', value, percent);
        }

        private static bool Renders(string number, char grouping, char decimalPoint, float value, bool percent)
        {
            string normalized = number.Replace(grouping.ToString(), "");
            if (normalized.IndexOf(decimalPoint) != normalized.LastIndexOf(decimalPoint))
            {
                return false;
            }
            int point = normalized.IndexOf(decimalPoint);
            int decimals = point < 0 ? 0 : normalized.Length - point - 1;
            double shown;
            if (!double.TryParse(normalized.Replace(decimalPoint, '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out shown))
            {
                return false;
            }
            double tolerance = 0.5 * Math.Pow(10, -decimals) + 0.0001;
            return Math.Abs(shown - value) <= tolerance
                || (percent && Math.Abs(shown - value * 100.0) <= tolerance);
        }

        /// <summary>The caption shares the control's line and ends at or before the control's left edge.</summary>
        public static bool CaptionsOnRow(LineBox caption, LineBox control)
        {
            return caption.Width > 0f && control.Width > 0f
                && OnLine(control, caption) && OnLine(caption, control)
                && caption.XMax <= control.X + EdgeSlack;
        }

        /// <summary>The icon sits on the caption's line, between the caption and the control.</summary>
        public static bool IconBetween(LineBox caption, LineBox icon, LineBox control)
        {
            return icon.Width > 0f && OnLine(icon, caption)
                && icon.X >= caption.XMax - EdgeSlack
                && icon.XMax <= control.X + EdgeSlack;
        }

        private static bool OnLine(LineBox box, LineBox line)
        {
            return box.CenterY >= line.Y && box.CenterY <= line.YMax;
        }
    }
}
