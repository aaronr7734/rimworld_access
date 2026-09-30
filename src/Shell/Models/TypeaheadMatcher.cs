using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// How a typeahead candidate interacts, ranked Control above Item above Text. Item is the
    /// default many screens rely on: an activatable list row with no explicit control role.
    /// </summary>
    public enum TypeaheadCandidateKind
    {
        Control,
        Item,
        Text,
    }

    /// <summary>Per-candidate ranking metadata: whether it sits in the group the cursor is already in, and how it interacts.</summary>
    public struct TypeaheadCandidate
    {
        public readonly bool Priority;
        public readonly TypeaheadCandidateKind Kind;

        /// <summary>
        /// A steadier identity for a row whose displayed label the game rewrites mid-session
        /// (vanilla relabels an architect row from "Wall..." to "Wooden wall"), typically the
        /// underlying def's own label. Null by default. It only ever ORDERS rows — see
        /// <see cref="TypeaheadMatcher"/>'s header.
        /// </summary>
        public readonly string Identity;

        /// <summary>Ranking band, compared right after the region key: a higher band never outranks a lower one. 0 default; table cell values sit in band 1.</summary>
        public readonly int Band;

        public TypeaheadCandidate(bool priority, TypeaheadCandidateKind kind)
            : this(priority, kind, null)
        {
        }

        public TypeaheadCandidate(bool priority, TypeaheadCandidateKind kind, string identity)
            : this(priority, kind, identity, 0)
        {
        }

        public TypeaheadCandidate(bool priority, TypeaheadCandidateKind kind, string identity, int band)
        {
            Priority = priority;
            Kind = kind;
            Identity = identity;
            Band = band;
        }
    }

    /// <summary>
    /// The typeahead matching algorithm. Every matched candidate ranks on nine ascending keys, in
    /// order: current region, ranking band, NAME hit over description-only, whole-name exactness, its
    /// interactable kind, the whole-word/first-word/other-word/substring tier, the name's word
    /// count, its length, and the original index. Only the region key depends on the cursor, and the
    /// index key makes the order total, so a fixed query over a fixed corpus always has one winner.
    ///
    /// The name-hit key outranks Control/Item/Text because that gap is the only one large enough to
    /// let a text row beat an interactable one, and the exact-name key outranks both: a player who
    /// typed a row's whole name left nothing to guess at. Within a tier a COMPLETE word beats a
    /// partial one wherever it sits ("wall" takes "Wooden wall" over "Wallpaper maker"), and the
    /// less qualified of two equal labels wins — fewer words first, then shorter, since length
    /// compares badly across languages that compound instead of qualifying. No rule knows any
    /// particular word, so it behaves the same in every translation, and matching is
    /// diacritic-insensitive on both sides.
    ///
    /// STABLE IDENTITY: <see cref="TypeaheadCandidate.Identity"/> contributes the exact-name and
    /// whole-word signals ONLY, and only as ordering — whether a row matches at all, how many
    /// matches the player is told about, and which key it lands under all stay the displayed label's
    /// business, so a row can only be promoted past rows the typed text already reaches on screen.
    ///
    /// MULTIWORD: a search containing spaces matches either as a literal contiguous phrase or as
    /// ordered word-prefix tokens, consumed left to right with gaps allowed, so "ga pi" finds "gas
    /// pipe". Trailing spaces are ignored while matching, so the space keystroke never turns a live
    /// match set into "no matches"; <see cref="AcceptsSearchChar"/> is the shared gate every scope's
    /// char sink applies.
    ///
    /// SUBSTRING FALLBACK, off by default: a scope whose rows are already narrowed by a vanilla or
    /// mod predicate that is itself a Contains test opts in through
    /// <see cref="TypeaheadModel.SubstringFallback"/>, admitting mid-word hits below every
    /// word-prefix tier. Without it the keyboard cannot reach rows that predicate leaves drawn.
    ///
    /// Pure: links into the test project.
    /// </summary>
    public static class TypeaheadMatcher
    {
        private static readonly char[] WordSeparators = { ' ', '-', '_', '(', ')', '[', ']', '/', '\\', '.', ',' };

        /// <summary>
        /// Whether a typed character extends a typeahead search or falls through to chord dispatch.
        /// Space extends only an ACTIVE search, never starts one, so an idle Space stays a chord;
        /// letters always search; digits search unless the screen reserves them as commands.
        /// </summary>
        public static bool AcceptsSearchChar(char c, bool searchActive, bool acceptDigits = true)
        {
            if (c == ' ')
                return searchActive;
            return char.IsLetter(c) || (acceptDigits && char.IsDigit(c));
        }

        /// <summary>
        /// A row's candidate kind from the shared role vocabulary. ReadOnly is the screen declaring
        /// the row is data, not a control; a role-less row stays an Item.
        /// </summary>
        public static TypeaheadCandidateKind ClassifyRow(ElementRole role, bool readOnly)
        {
            if (readOnly)
                return TypeaheadCandidateKind.Text;
            switch (role)
            {
                case ElementRole.Button:
                case ElementRole.Checkbox:
                case ElementRole.RadioButton:
                case ElementRole.ComboBox:
                case ElementRole.Slider:
                case ElementRole.Stepper:
                case ElementRole.TextField:
                case ElementRole.Tab:
                case ElementRole.Map:
                    return TypeaheadCandidateKind.Control;
                default:
                    return TypeaheadCandidateKind.Item;
            }
        }

        /// <summary>All label indices matching the search, best first; an empty search or list yields none.</summary>
        public static List<int> FindMatches(string search, IReadOnlyList<string> labels)
            => FindMatches(search, labels, null, false);

        /// <summary>
        /// As above, with ranking metadata parallel to <paramref name="labels"/>; a null or short
        /// list means default metadata for the missing entries.
        /// </summary>
        public static List<int> FindMatches(string search, IReadOnlyList<string> labels,
            IReadOnlyList<TypeaheadCandidate> candidates)
            => FindMatches(search, labels, candidates, false);

        /// <summary>
        /// As above; <paramref name="substringFallback"/> also admits MID-WORD hits, ranked last;
        /// <paramref name="documentOrder"/> keeps row order within the region priority instead.
        /// </summary>
        public static List<int> FindMatches(string search, IReadOnlyList<string> labels,
            IReadOnlyList<TypeaheadCandidate> candidates, bool substringFallback, bool documentOrder = false)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(search) || labels == null)
                return result;

            // Trailing spaces are buffer state, not query content, so the space keystroke keeps the
            // current match set while the user types toward the next word.
            string searchLower = TextNormalization.RemoveDiacritics(search.ToLowerInvariant()).TrimEnd();
            if (searchLower.Length == 0)
                return result;
            string[] searchTokens = searchLower.IndexOf(' ') >= 0
                ? searchLower.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries)
                : null;

            var matches = new List<MatchEntry>();
            for (int i = 0; i < labels.Count; i++)
            {
                if (string.IsNullOrEmpty(labels[i]))
                    continue;
                LabelParts parts = SplitLabel(labels[i]);
                MatchType matchType = GetMatchType(searchLower, searchTokens, parts, substringFallback);
                if (matchType == MatchType.None)
                    continue;

                TypeaheadCandidate candidate = candidates != null && i < candidates.Count
                    ? candidates[i]
                    : new TypeaheadCandidate(false, TypeaheadCandidateKind.Item);
                int exact = matchType == MatchType.ExactName ? 0 : 1;
                int tier = TierOf(matchType);
                PromoteByIdentity(searchLower, searchTokens, candidate.Identity, ref exact, ref tier);
                matches.Add(new MatchEntry
                {
                    Index = i,
                    Priority = candidate.Priority ? 0 : 1,
                    Band = candidate.Band,
                    NameMatch = matchType == MatchType.Description ? 1 : 0,
                    Exact = exact,
                    Kind = (int)candidate.Kind,
                    Tier = tier,
                    WordCount = parts.NameWords.Length,
                    NameLength = parts.NameText.Length,
                });
            }

            matches.Sort(documentOrder ? (Comparison<MatchEntry>)CompareDocumentOrder : CompareEntries);
            for (int i = 0; i < matches.Count; i++)
                result.Add(matches[i].Index);
            return result;
        }

        private struct MatchEntry
        {
            public int Index;
            public int Priority;
            public int Band;
            public int NameMatch;
            public int Exact;
            public int Kind;
            public int Tier;
            public int WordCount;
            public int NameLength;
        }

        private enum MatchType
        {
            None,
            ExactName,   // The query is the whole name, separators aside
            WholeWord,   // A complete word of the name, wherever it sits
            FirstWord,   // Partial match at the start of the name or its first word
            OtherWord,   // Partial match on a later word of the name
            Description, // Match only outside the name portion
            Substring,   // Mid-word only; opt-in, ranked last
        }

        /// <summary>
        /// Lifts the exact and tier keys of a row whose stable identity the query hits more squarely
        /// than its displayed label. Only the two whole-word signals carry over, and the keys only
        /// ever improve, so an identity can never demote the row that owns it.
        /// </summary>
        private static void PromoteByIdentity(string searchLower, string[] searchTokens,
            string identity, ref int exact, ref int tier)
        {
            if (string.IsNullOrEmpty(identity))
                return;
            switch (GetMatchType(searchLower, searchTokens, SplitLabel(identity), false))
            {
                case MatchType.ExactName:
                    exact = 0;
                    tier = 0;
                    break;
                case MatchType.WholeWord:
                    tier = 0;
                    break;
            }
        }

        /// <summary>
        /// The tier key. Exact and whole-word share the top tier, since exactness has its own higher
        /// key, and description stays at 0 because the name-hit key already separated it.
        /// </summary>
        private static int TierOf(MatchType matchType)
        {
            switch (matchType)
            {
                case MatchType.FirstWord: return 1;
                case MatchType.OtherWord: return 2;
                case MatchType.Substring: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// One label, normalized once per keystroke: the whole label, its name portion (before the
        /// first ": " or ". ", parentheticals stripped so descriptions cannot pollute the name
        /// tiers), and that portion's words.
        /// </summary>
        private struct LabelParts
        {
            public string Full;
            public string NameText;
            public string[] NameWords;
        }

        private static LabelParts SplitLabel(string label)
        {
            string labelLower = TextNormalization.RemoveDiacritics(label.ToLowerInvariant().Trim());
            int boundary = FindNameBoundary(labelLower);
            string namePortion = boundary >= 0 ? labelLower.Substring(0, boundary) : labelLower;
            string nameText = StripParentheticalContent(namePortion);
            return new LabelParts
            {
                Full = labelLower,
                NameText = nameText,
                NameWords = nameText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries),
            };
        }

        /// <summary>
        /// The strongest tier the query reaches on one label. Whole-word tiers are tested before the
        /// prefix ones, which would otherwise mask them.
        /// </summary>
        private static MatchType GetMatchType(string searchLower, string[] searchTokens,
            LabelParts parts, bool substringFallback)
        {
            string[] nameWords = parts.NameWords;

            if (searchTokens != null)
            {
                int firstEqual = MatchTokensInOrder(nameWords, searchTokens, WordEquals);
                if (firstEqual == 0 && nameWords.Length == searchTokens.Length)
                    return MatchType.ExactName;
                if (firstEqual >= 0)
                    return MatchType.WholeWord;
            }
            else
            {
                for (int i = 0; i < nameWords.Length; i++)
                {
                    if (WordEquals(nameWords[i], searchLower))
                        return nameWords.Length == 1 ? MatchType.ExactName : MatchType.WholeWord;
                }
            }

            if (parts.NameText.StartsWith(searchLower, StringComparison.Ordinal))
                return MatchType.FirstWord;

            if (searchTokens != null)
            {
                int firstWord = MatchTokensInOrder(nameWords, searchTokens, WordStartsWith);
                if (firstWord == 0)
                    return MatchType.FirstWord;
                if (firstWord > 0)
                    return MatchType.OtherWord;

                string[] allLabelWords = parts.Full.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (MatchTokensInOrder(allLabelWords, searchTokens, WordStartsWith) >= 0)
                    return MatchType.Description;

                // A spaced query gets the same mid-word leniency, token by token. No early return: a
                // query with a WordSeparator inside a token fits no per-word test but can still hit
                // the whole-label fallback below.
                if (substringFallback && MatchTokensInOrder(allLabelWords, searchTokens, WordContains) >= 0)
                    return MatchType.Substring;
            }
            else
            {
                if (nameWords.Length > 0 && nameWords[0].StartsWith(searchLower, StringComparison.Ordinal))
                    return MatchType.FirstWord;

                for (int i = 1; i < nameWords.Length; i++)
                {
                    if (nameWords[i].StartsWith(searchLower, StringComparison.Ordinal))
                        return MatchType.OtherWord;
                }

                string[] allWords = parts.Full.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < allWords.Length; i++)
                {
                    if (allWords[i].StartsWith(searchLower, StringComparison.Ordinal))
                        return MatchType.Description;
                }
            }

            if (substringFallback && parts.Full.IndexOf(searchLower, StringComparison.Ordinal) >= 0)
                return MatchType.Substring;

            return MatchType.None;
        }

        private static readonly char[] SpaceSeparator = { ' ' };

        // Cached in fields, not passed as method groups: MatchTokensInOrder takes one per label per
        // keystroke, and a method-group conversion would allocate a delegate on every call.
        private static readonly Func<string, string, bool> WordEquals =
            (word, token) => string.Equals(word, token, StringComparison.Ordinal);

        private static readonly Func<string, string, bool> WordStartsWith =
            (word, token) => word.StartsWith(token, StringComparison.Ordinal);

        private static readonly Func<string, string, bool> WordContains =
            (word, token) => word.IndexOf(token, StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Index of the word the first token matched when every token <paramref name="fits"/> a
        /// distinct word, consumed left to right with gaps allowed ("ga pi" fits "gas metal pipe");
        /// -1 otherwise. Order is enforced, so "pi ga" never fits "gas pipe".
        /// </summary>
        private static int MatchTokensInOrder(string[] words, string[] tokens, Func<string, string, bool> fits)
        {
            if (tokens.Length == 0 || words.Length == 0)
                return -1;
            int tokenIndex = 0;
            int firstWord = -1;
            for (int w = 0; w < words.Length && tokenIndex < tokens.Length; w++)
            {
                if (fits(words[w], tokens[tokenIndex]))
                {
                    if (tokenIndex == 0)
                        firstWord = w;
                    tokenIndex++;
                }
            }
            return tokenIndex == tokens.Length ? firstWord : -1;
        }

        /// <summary>First ": " or ". " wins; -1 when the whole label is name.</summary>
        private static int FindNameBoundary(string lowerText)
        {
            int colon = lowerText.IndexOf(": ", StringComparison.Ordinal);
            int period = lowerText.IndexOf(". ", StringComparison.Ordinal);
            if (colon < 0)
                return period;
            if (period < 0)
                return colon;
            return Math.Min(colon, period);
        }

        /// <summary>
        /// The nine ranking keys in order. Index last makes the comparison total, which
        /// List&lt;T&gt;.Sort's unstable order requires.
        /// </summary>
        private static int CompareEntries(MatchEntry a, MatchEntry b)
        {
            int cmp = a.Priority.CompareTo(b.Priority);
            if (cmp == 0)
                cmp = a.Band.CompareTo(b.Band);
            if (cmp == 0)
                cmp = a.NameMatch.CompareTo(b.NameMatch);
            if (cmp == 0)
                cmp = a.Exact.CompareTo(b.Exact);
            if (cmp == 0)
                cmp = a.Kind.CompareTo(b.Kind);
            if (cmp == 0)
                cmp = a.Tier.CompareTo(b.Tier);
            if (cmp == 0)
                cmp = a.WordCount.CompareTo(b.WordCount);
            if (cmp == 0)
                cmp = a.NameLength.CompareTo(b.NameLength);
            if (cmp == 0)
                cmp = a.Index.CompareTo(b.Index);
            return cmp;
        }

        private static int CompareDocumentOrder(MatchEntry a, MatchEntry b)
        {
            int cmp = a.Priority.CompareTo(b.Priority);
            if (cmp == 0)
                cmp = a.Band.CompareTo(b.Band);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        }

        /// <summary>"Sleeping spot (description here)" → "Sleeping spot"; handles nesting.</summary>
        private static string StripParentheticalContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var result = new System.Text.StringBuilder();
            int depth = 0;
            foreach (char c in text)
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth > 0)
                        depth--;
                }
                else if (depth == 0)
                {
                    result.Append(c);
                }
            }
            return result.ToString().Trim();
        }
    }
}
