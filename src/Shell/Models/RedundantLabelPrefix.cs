namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Removes a supplementary string's opening repetition of the label it accompanies. A row that
    /// speaks its name and then a tooltip opening with that same name says it twice; a sighted
    /// player reads the name once, on the control. Callers strip rich-text tags first.
    /// </summary>
    public static class RedundantLabelPrefix
    {
        private static readonly char[] SentenceBreaks = { '.', ':', '!', '?', '\n' };

        /// <summary>
        /// Drops a leading <paramref name="label"/> followed by a sentence break (or the end), and a
        /// whole first line the label itself opens with ("Strong" over "Strong (good)"). Whole
        /// sentences and words only, so "Hotkeys are disabled..." under "Hotkeys" keeps every character.
        /// </summary>
        public static string Strip(string extras, string label)
        {
            if (string.IsNullOrEmpty(extras) || string.IsNullOrEmpty(label))
            {
                return extras;
            }

            string trimmed = extras.TrimStart();
            if (!trimmed.StartsWith(label, System.StringComparison.Ordinal))
            {
                return StripTitleLine(extras, trimmed, label);
            }

            int afterLabel = label.Length;
            if (afterLabel == trimmed.Length)
            {
                return "";
            }

            char next = trimmed[afterLabel];
            bool isBreak = false;
            foreach (char c in SentenceBreaks)
            {
                if (next == c)
                {
                    isBreak = true;
                    break;
                }
            }
            if (!isBreak)
            {
                return extras;
            }

            string remainder = trimmed.Substring(afterLabel + 1).TrimStart();
            return remainder;
        }

        private static string StripTitleLine(string extras, string trimmed, string label)
        {
            int lineEnd = trimmed.IndexOf('\n');
            string firstLine = (lineEnd < 0 ? trimmed : trimmed.Substring(0, lineEnd)).TrimEnd();
            if (firstLine.Length == 0 || firstLine.Length >= label.Length
                || !label.StartsWith(firstLine, System.StringComparison.Ordinal)
                || char.IsLetterOrDigit(label[firstLine.Length]))
            {
                return extras;
            }
            return lineEnd < 0 ? "" : trimmed.Substring(lineEnd + 1).TrimStart();
        }

        /// <summary>
        /// A tooltip whose first line opens with its <paramref name="title"/>, minus that title and
        /// any ':' after it; the rest of the line ("(age 30)") and every later line stay.
        /// </summary>
        public static string AfterTitle(string tooltip, string title)
        {
            if (string.IsNullOrEmpty(tooltip) || string.IsNullOrEmpty(title))
            {
                return tooltip;
            }
            string trimmed = tooltip.TrimStart();
            if (!trimmed.StartsWith(title, System.StringComparison.Ordinal))
            {
                return tooltip;
            }
            return trimmed.Substring(title.Length).TrimStart(':', ' ', '\t').TrimStart('\r', '\n');
        }
    }
}
