using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimWorldAccess
{
    /// <summary>
    /// Generic scanner search filtering and ranking. Consolidates the FirstWord / OtherWord /
    /// Contains three-bucket match-type logic that was previously duplicated four times in
    /// ScannerSearchState (for Map/World × Update/Refresh). Works for any item type that
    /// exposes a label and a distance projection.
    /// </summary>
    public static class ScannerSearchEngine
    {
        public enum MatchType
        {
            None,
            FirstWord,  // Match at start of label / first word
            OtherWord,  // Match on a non-first word in the name
            Contains    // Match anywhere in label
        }

        private static readonly char[] WordSeparators =
            { ' ', '-', '_', '(', ')', '[', ']', '/', '\\', '.', ',' };

        /// <summary>
        /// Filters and ranks items by match quality against the query, then sorts each
        /// quality tier by distance and concatenates FirstWord &gt; OtherWord &gt; Contains.
        /// Returns an empty list when the query is empty.
        /// </summary>
        public static List<T> FilterAndRank<T>(
            IEnumerable<T> items,
            string query,
            Func<T, string> labelSelector,
            Func<T, float> distanceSelector)
        {
            if (string.IsNullOrEmpty(query) || items == null)
                return new List<T>();

            var firstWord = new List<T>();
            var otherWord = new List<T>();
            var contains = new List<T>();

            foreach (var item in items)
            {
                switch (GetMatchType(query, labelSelector(item)))
                {
                    case MatchType.FirstWord: firstWord.Add(item); break;
                    case MatchType.OtherWord: otherWord.Add(item); break;
                    case MatchType.Contains: contains.Add(item); break;
                }
            }

            return firstWord.OrderBy(distanceSelector)
                .Concat(otherWord.OrderBy(distanceSelector))
                .Concat(contains.OrderBy(distanceSelector))
                .ToList();
        }

        /// <summary>
        /// Determines the match quality between a search string and a candidate label.
        /// Uses the same prioritization as TypeaheadSearchHelper.
        /// </summary>
        public static MatchType GetMatchType(string search, string label)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(label))
                return MatchType.None;

            string searchLower = search.ToLowerInvariant();
            string labelLower = label.ToLowerInvariant().Trim();

            string nameOnly = StripParentheticalContent(labelLower);

            if (nameOnly.StartsWith(searchLower))
                return MatchType.FirstWord;

            string[] nameWords = nameOnly.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (nameWords.Length > 0 && nameWords[0].StartsWith(searchLower))
                return MatchType.FirstWord;

            for (int i = 1; i < nameWords.Length; i++)
            {
                if (nameWords[i].StartsWith(searchLower))
                    return MatchType.OtherWord;
            }

            if (labelLower.Contains(searchLower))
                return MatchType.Contains;

            return MatchType.None;
        }

        /// <summary>
        /// Strips parenthetical content from a label (e.g., "double bed (fine)" → "double bed").
        /// Handles nested parentheses.
        /// </summary>
        public static string StripParentheticalContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var result = new StringBuilder();
            int depth = 0;

            foreach (char c in text)
            {
                if (c == '(')
                    depth++;
                else if (c == ')')
                {
                    if (depth > 0) depth--;
                }
                else if (depth == 0)
                    result.Append(c);
            }

            return result.ToString().Trim();
        }
    }
}
