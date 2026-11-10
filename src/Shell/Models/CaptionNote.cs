using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Text shapes the generic reader uses to tell a control's caption from the lines drawn
    /// around it: a dimmed note under the caption, and a label redrawn over a folded pair.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class CaptionNote
    {
        private const string ColorOpen = "<color=";
        private const string ColorClose = "</color>";

        /// <summary>The whole text sits inside one colour span: a dimmed note, never a caption with a coloured value.</summary>
        public static bool IsWholeColorSpan(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string t = text.Trim();
            if (!t.StartsWith(ColorOpen, StringComparison.OrdinalIgnoreCase)
                || !t.EndsWith(ColorClose, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            int openEnd = t.IndexOf('>');
            int firstClose = t.IndexOf(ColorClose, StringComparison.OrdinalIgnoreCase);
            return openEnd > 0 && firstClose == t.Length - ColorClose.Length
                && t.IndexOf(ColorOpen, 1, StringComparison.OrdinalIgnoreCase) < 0
                && t.Substring(openEnd + 1, firstClose - openEnd - 1).Trim().Length > 0;
        }

        private const float NoteGapMax = 8f;
        private const float DimChannelMax = 0.75f;
        private const float GreySpreadMax = 0.1f;
        private const float DimAlphaMax = 0.75f;

        /// <summary>
        /// A line of several counters ("Chars: 0 | Tokens: ~0", "ABM 0 · SCM 0 · ELS 0"): three or more
        /// numbers, or two split by a bar or dot. A readout, never the name of a control.
        /// </summary>
        public static bool IsMultiValueReadout(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            int runs = 0;
            bool separated = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    if (i == 0 || !ContinuesNumber(text, i - 1))
                    {
                        runs++;
                    }
                    continue;
                }
                if (c == '|' || c == '｜' || c == '·' || c == '•')
                {
                    separated = true;
                }
            }
            return runs >= 3 || (runs >= 2 && separated);
        }

        /// <summary>A plain caption line that may name a control: anything but a counter readout.</summary>
        public static bool NamesControl(string text)
        {
            return !IsMultiValueReadout(text);
        }

        // The character before a digit belongs to the same number: a digit, or a decimal/group mark between digits.
        private static bool ContinuesNumber(string text, int index)
        {
            char c = text[index];
            if (char.IsDigit(c))
            {
                return true;
            }
            return (c == '.' || c == ',') && index > 0 && char.IsDigit(text[index - 1]);
        }

        /// <summary>Text drawn in a grey or faded tint: a secondary note, never a caption.</summary>
        public static bool IsDimmedTint(float r, float g, float b, float a)
        {
            if (a <= DimAlphaMax)
            {
                return true;
            }
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            return max <= DimChannelMax && max - min <= GreySpreadMax;
        }

        /// <summary>The note sits directly under the control, a few pixels below it, overlapping its column.</summary>
        public static bool SitsUnder(LineBox control, LineBox note)
        {
            if (control.Width <= 0f || note.Width <= 0f)
            {
                return false;
            }
            float gap = note.GapBelow(control);
            return gap >= -LineBox.EdgeSlack && gap <= NoteGapMax && note.OverlapsColumn(control);
        }

        /// <summary>
        /// A label drawn again inside a folded name/value pair's box, on its line, carrying one of
        /// the pair's own segments.
        /// </summary>
        public static bool RedrawsPairSegment(LineBox pair, string pairText, string pairName, LineBox segment, string segmentText)
        {
            if (string.IsNullOrWhiteSpace(segmentText) || string.IsNullOrEmpty(pairText)
                || pair.Width <= 0f || segment.Width <= 0f)
            {
                return false;
            }
            if (!segment.Inside(pair) || !segment.OnLineOf(pair) || string.IsNullOrWhiteSpace(pairName))
            {
                return false;
            }
            string text = segmentText.Trim();
            string name = pairName.Trim();
            if (string.Equals(text, name, StringComparison.Ordinal))
            {
                return true;
            }
            string words = pairText.Trim();
            if (!words.StartsWith(name, StringComparison.Ordinal))
            {
                return false;
            }
            string rest = words.Substring(name.Length).TrimStart(':', '：', ' ');
            return string.Equals(rest, text, StringComparison.Ordinal);
        }
    }
}
