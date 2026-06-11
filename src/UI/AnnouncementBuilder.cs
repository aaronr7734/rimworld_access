using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess
{
    public enum Separator
    {
        Period,
        Comma,
        Colon,
        Semicolon,
        Space,
    }

    /// <summary>
    /// Composes a screen reader announcement from clean phrase fragments,
    /// owning the punctuation between fragments instead of relying on each
    /// fragment to carry its own trailing separator.
    ///
    /// Replaces the StringBuilder + AppendLine pattern that violates the
    /// "no newlines as separators" rule.
    /// </summary>
    public class AnnouncementBuilder
    {
        private readonly StringBuilder sb = new StringBuilder();
        // Each fragment added via Add/Label, retained as a discrete row so callers
        // that need a navigable list (one stat per row) can get the same content
        // Build() flattens into a single period-joined announcement.
        private readonly List<string> rows = new List<string>();
        private Separator defaultSep = Separator.Period;
        private bool hasContent = false;

        public AnnouncementBuilder DefaultSep(Separator s)
        {
            defaultSep = s;
            return this;
        }

        public AnnouncementBuilder Add(string text)
        {
            return Add(text, defaultSep);
        }

        public AnnouncementBuilder Add(string text, Separator sep)
        {
            if (string.IsNullOrEmpty(text))
                return this;

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
                return this;

            if (hasContent)
                AppendSeparator(sep);

            sb.Append(trimmed);
            rows.Add(trimmed);
            hasContent = true;
            return this;
        }

        public AnnouncementBuilder AddIf(bool cond, string text)
        {
            return cond ? Add(text) : this;
        }

        public AnnouncementBuilder AddIf(bool cond, string text, Separator sep)
        {
            return cond ? Add(text, sep) : this;
        }

        public AnnouncementBuilder Label(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
                return this;

            string trimmedLabel = label?.Trim() ?? string.Empty;
            string trimmedValue = value.Trim();
            if (trimmedValue.Length == 0)
                return this;

            if (hasContent)
                AppendSeparator(defaultSep);

            string row = trimmedLabel.Length > 0 ? $"{trimmedLabel}: {trimmedValue}" : trimmedValue;
            sb.Append(row);
            rows.Add(row);
            hasContent = true;
            return this;
        }

        private void AppendSeparator(Separator sep)
        {
            switch (sep)
            {
                case Separator.Period:    sb.Append(". "); break;
                case Separator.Comma:     sb.Append(", "); break;
                case Separator.Colon:     sb.Append(": "); break;
                case Separator.Semicolon: sb.Append("; "); break;
                case Separator.Space:     sb.Append(' ');  break;
            }
        }

        public string Build()
        {
            if (!hasContent)
                return string.Empty;

            while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
                sb.Length--;

            if (sb.Length == 0)
                return string.Empty;

            char last = sb[sb.Length - 1];
            if (last != '.' && last != '!' && last != '?' && last != ':' && last != ';' && last != ',')
                sb.Append('.');

            return sb.ToString();
        }

        /// <summary>
        /// Returns each added fragment as a discrete row, in insertion order. Use when the
        /// announcement should be navigated one item at a time (e.g. an arrow-key stat list)
        /// rather than spoken as a single flattened string via <see cref="Build"/>.
        /// </summary>
        public List<string> BuildLines()
        {
            return new List<string>(rows);
        }
    }
}
