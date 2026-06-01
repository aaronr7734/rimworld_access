using System.Text.RegularExpressions;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralized text sanitization pipeline for screen reader output.
    /// Runs automatically in TolkHelper.Speak() before text reaches the screen reader.
    /// Handles tag stripping, punctuation cleanup, and whitespace normalization.
    /// </summary>
    public static class SpeechSanitizer
    {
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex NewlineDotRegex = new Regex(@"\n[ \t]*([\.,;:])", RegexOptions.Compiled);
        private static readonly Regex MultiNewlineRegex = new Regex(@"\n+", RegexOptions.Compiled);
        private static readonly Regex PeriodSpacePeriodRegex = new Regex(@"\.[ \t]+\.", RegexOptions.Compiled);
        // Whitespace before sentence punctuation is never correct in English and reads as a stray
        // "period"/"comma" (e.g. a row built from "{label} . {value}" where the label's own value was
        // empty). Collapse the space(s) onto the punctuation: "Blindness . Horrible" -> "Blindness. Horrible".
        private static readonly Regex SpaceBeforePunctuationRegex = new Regex(@"[ \t]+([\.,;:])", RegexOptions.Compiled);
        // Sentence punctuation at the very start of a line has nothing to terminate and reads as a stray
        // "period"/"comma" (e.g. ". Suppression: 50%" from a row built with a leading separator). Drop it.
        private static readonly Regex LeadingPunctuationRegex = new Regex(@"^[ \t]*[\.,;:]+[ \t]*", RegexOptions.Compiled);

        private const string EllipsisPlaceholder = "\x01ELLIPSIS\x01";

        /// <summary>
        /// Sanitizes text for screen reader output. Called automatically by TolkHelper.Speak().
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = StripTags(text);
            text = text.Replace("<", "").Replace(">", "");
            text = NormalizeNewlines(text);
            text = CollapseSpaces(text);

            text = text.Replace("...", EllipsisPlaceholder);
            text = FixBadPunctuation(text);
            text = FixDoublePeriods(text);
            // Strip leading orphan punctuation while the ellipsis is still masked, so a legitimate
            // leading "..." is preserved.
            text = LeadingPunctuationRegex.Replace(text, "");
            text = text.Replace(EllipsisPlaceholder, "...");

            text = text.Replace("....", "...");

            return text.Trim();
        }

        /// <summary>
        /// Normalizes line endings and folds stray newlines into sentence breaks,
        /// dropping any redundant punctuation that follows a newline.
        /// </summary>
        private static string NormalizeNewlines(string text)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            // A newline acts as a sentence break already; drop the redundant
            // punctuation that immediately follows so we don't get "+2%[break]. text"
            text = NewlineDotRegex.Replace(text, "$1");
            text = MultiNewlineRegex.Replace(text, "\n");
            text = text.Replace("\n", ". ");
            return text;
        }

        private static string StripTags(string text)
        {
            return TagRegex.Replace(text, "");
        }

        private static string CollapseSpaces(string text)
        {
            return MultiSpaceRegex.Replace(text, " ");
        }

        private static string FixBadPunctuation(string text)
        {
            text = SpaceBeforePunctuationRegex.Replace(text, "$1");
            text = text.Replace(":.", ".");
            text = text.Replace(".,", ".");
            text = text.Replace(",.", ".");
            text = text.Replace(":,", ":");
            text = text.Replace(",:", ":");
            return text;
        }

        private static string FixDoublePeriods(string text)
        {
            text = text.Replace("..", ".");
            text = PeriodSpacePeriodRegex.Replace(text, ".");
            return text;
        }
    }
}
