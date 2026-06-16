using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for typeahead search functionality in menus.
    /// Each menu creates its own instance to manage search state.
    /// </summary>
    public class TypeaheadSearchHelper
    {
        // Configuration
        private const float AUTO_RESET_SECONDS = 3.0f;

        /// <summary>
        /// When true, the per-keystroke auto-reset timeout is ignored — the search buffer persists
        /// until explicitly cleared. Set while an explicit CJK/IME search session is open
        /// (see <c>MenuSearchState</c>), where each character can take seconds to compose through
        /// the OS candidate window and the 3-second instant-typeahead timeout would otherwise wipe
        /// a multi-character query between commits. Applies to every typeahead consumer because they
        /// all share this helper (TabularMenuHelper, TreeNavigationHelper, TradeNavigationState, …).
        /// </summary>
        public static bool SuppressAutoReset { get; set; }

        // Word separators for prefix matching
        private static readonly char[] WordSeparators = { ' ', '-', '_', '(', ')', '[', ']', '/', '\\', '.', ',' };

        // State
        private string searchBuffer = "";
        private string lastFailedSearch = "";  // Stores the search that had no matches (for announcement)
        private float lastInputTime = 0f;
        private List<int> matchingIndices = new List<int>();
        private int currentMatchIndex = 0;

        /// <summary>
        /// Returns true if there is an active search (buffer not empty).
        /// </summary>
        public bool HasActiveSearch => !string.IsNullOrEmpty(searchBuffer);

        /// <summary>
        /// Returns true if there is an active search with no matches.
        /// </summary>
        public bool HasNoMatches => !string.IsNullOrEmpty(searchBuffer) && matchingIndices.Count == 0;

        /// <summary>
        /// Gets the current search string.
        /// </summary>
        public string SearchBuffer => searchBuffer;

        /// <summary>
        /// Gets the last search string that had no matches (for announcement after auto-clear).
        /// </summary>
        public string LastFailedSearch => lastFailedSearch;

        /// <summary>
        /// Gets the number of current matches.
        /// </summary>
        public int MatchCount => matchingIndices.Count;

        /// <summary>
        /// Gets the 1-based position within matches for announcements.
        /// </summary>
        public int CurrentMatchPosition => matchingIndices.Count > 0 ? currentMatchIndex + 1 : 0;

        /// <summary>
        /// Gets the list of matching indices.
        /// </summary>
        public List<int> MatchingIndices => matchingIndices;

        /// <summary>
        /// Processes a character input for typeahead search.
        /// </summary>
        /// <param name="c">The character typed</param>
        /// <param name="labels">List of item labels to search</param>
        /// <param name="newIndex">Output: the index to navigate to, or -1 if no matches</param>
        /// <returns>True if input was processed successfully with matches, false if no matches found</returns>
        public bool ProcessCharacterInput(char c, List<string> labels, out int newIndex)
        {
            newIndex = -1;

            float currentTime = Time.realtimeSinceStartup;

            // Check timeout FIRST with current time (skipped during an explicit CJK search session,
            // where slow IME composition between commits must not wipe the in-progress query).
            if (!SuppressAutoReset && HasActiveSearch && currentTime - lastInputTime > AUTO_RESET_SECONDS)
            {
                ClearSearch();
            }

            // Update time immediately
            lastInputTime = currentTime;
            searchBuffer += c;

            // Find all matching items using two-pass approach:
            // Pass 1: Match against names only (ignore parenthetical content like descriptions)
            // Pass 2: If no matches, try matching full labels including parenthetical content
            FindMatches(labels);

            // If matches found, set newIndex to first match
            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
                return true;
            }

            // No matches - store the failed search for announcement, then auto-clear
            lastFailedSearch = searchBuffer;
            ClearSearch();
            return false;
        }

        /// <summary>
        /// Processes backspace to remove the last character from search buffer.
        /// </summary>
        /// <param name="labels">List of item labels to search</param>
        /// <param name="newIndex">Output: the index to navigate to, or -1 if search cleared</param>
        /// <returns>True if backspace was handled</returns>
        public bool ProcessBackspace(List<string> labels, out int newIndex)
        {
            newIndex = -1;

            if (!HasActiveSearch)
            {
                return false;
            }

            // Remove last character
            searchBuffer = searchBuffer.Substring(0, searchBuffer.Length - 1);
            lastInputTime = Time.realtimeSinceStartup;

            // If buffer is now empty, clear search entirely
            if (string.IsNullOrEmpty(searchBuffer))
            {
                ClearSearch();
                return true;
            }

            // Re-filter matches using two-pass approach
            FindMatches(labels);

            // Update current match index and return first match
            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
            }

            return true;
        }

        /// <summary>
        /// Clears the search state entirely.
        /// </summary>
        public void ClearSearch()
        {
            searchBuffer = "";
            matchingIndices.Clear();
            currentMatchIndex = 0;
        }

        /// <summary>
        /// Clears the search and announces "Search cleared" via TolkHelper.
        /// Returns true if there was an active search to clear, false otherwise.
        /// Use this when the user explicitly clears the search (e.g., pressing Escape).
        /// </summary>
        public bool ClearSearchAndAnnounce()
        {
            if (!HasActiveSearch)
                return false;

            ClearSearch();
            TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
            return true;
        }

        // ===== Search Announcements =====

        /// <summary>
        /// Announces the canonical "No matches for '{last failed search}'" message
        /// after an input has auto-cleared the search because nothing matched.
        /// Callers should check <see cref="HasNoMatches"/> or detect the failed
        /// input themselves — this helper only formats the announcement.
        /// </summary>
        public void SpeakNoMatches(SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(lastFailedSearch), priority);
        }

        /// <summary>
        /// Returns the canonical search-context suffix for the current match:
        /// ", {pos} of {count} matches for '{buffer}'". Returns an empty string
        /// when no search is active. The leading ", " makes the suffix safe to
        /// append directly to a label.
        /// </summary>
        public string BuildSearchContextSuffix()
        {
            if (!HasActiveSearch || matchingIndices.Count == 0) return "";
            return "RimWorldAccess.Search.ContextSuffix".Translate(CurrentMatchPosition, MatchCount, searchBuffer).ToString();
        }

        /// <summary>
        /// Builds "{itemLabel}{search suffix}" — a full item announcement with the
        /// match-position context appended when a search is active, or just the
        /// raw label when it isn't.
        /// </summary>
        public string BuildItemAnnouncement(string itemLabel)
        {
            return (itemLabel ?? "") + BuildSearchContextSuffix();
        }

        /// <summary>
        /// Speaks the item announcement when matches exist, or falls back to the
        /// canonical "No matches" announcement when the last input failed to match.
        /// </summary>
        public void SpeakItemAnnouncement(string itemLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (HasNoMatches || (HasActiveSearch && matchingIndices.Count == 0))
            {
                SpeakNoMatches(priority);
                return;
            }
            TolkHelper.SpeakData(BuildItemAnnouncement(itemLabel), priority);
        }

        /// <summary>
        /// Finds matching items with priority ordering:
        /// 1. First word prefix matches (e.g., 'w' matches "Wall" before "5 wood")
        /// 2. Other word prefix matches in the name (before parenthetical content)
        /// 3. Matches in parenthetical content (descriptions) as fallback
        ///
        /// Within each tier, matches are sorted by name length ascending so closer
        /// matches win (e.g., "Wall" ranks above "Wall lamp" when searching "wall").
        /// </summary>
        private void FindMatches(List<string> labels)
        {
            matchingIndices.Clear();

            // Each entry: (originalIndex, nameLength) — we sort by nameLength ascending,
            // then by originalIndex as a stable tiebreak.
            var firstWordMatches = new List<(int idx, int nameLen)>();
            var otherWordMatches = new List<(int idx, int nameLen)>();
            var descriptionMatches = new List<(int idx, int nameLen)>();

            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i];
                MatchType matchType = GetMatchType(searchBuffer, label);

                if (matchType == MatchType.None)
                    continue;

                int nameLen = GetNameLength(label);

                switch (matchType)
                {
                    case MatchType.FirstWord:
                        firstWordMatches.Add((i, nameLen));
                        break;
                    case MatchType.OtherWord:
                        otherWordMatches.Add((i, nameLen));
                        break;
                    case MatchType.Description:
                        descriptionMatches.Add((i, nameLen));
                        break;
                }
            }

            SortByNameLength(firstWordMatches);
            SortByNameLength(otherWordMatches);
            SortByNameLength(descriptionMatches);

            foreach (var m in firstWordMatches) matchingIndices.Add(m.idx);
            foreach (var m in otherWordMatches) matchingIndices.Add(m.idx);
            foreach (var m in descriptionMatches) matchingIndices.Add(m.idx);
        }

        /// <summary>
        /// Returns the length of the displayed "name" portion of a label — the part
        /// before the first ": " or ". " boundary, with parenthetical hints stripped.
        /// Used to rank closer matches first within a tier.
        /// </summary>
        private static int GetNameLength(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            string lower = label.ToLowerInvariant().Trim();
            int boundary = FindNameBoundary(lower);
            string namePortion = boundary >= 0 ? lower.Substring(0, boundary) : lower;
            return StripParentheticalContent(namePortion).TrimEnd().Length;
        }

        /// <summary>
        /// Finds the boundary between the "name" portion of a label and its
        /// annotation/content portion. The first occurrence of ": " or ". " wins
        /// (matches architect format: "Name: cost. description" or "Name. description").
        /// Returns -1 if no boundary is found (the whole label is name).
        /// </summary>
        private static int FindNameBoundary(string lowerText)
        {
            int colon = lowerText.IndexOf(": ");
            int period = lowerText.IndexOf(". ");
            if (colon < 0) return period;
            if (period < 0) return colon;
            return Math.Min(colon, period);
        }

        private static void SortByNameLength(List<(int idx, int nameLen)> list)
        {
            list.Sort((a, b) =>
            {
                int cmp = a.nameLen.CompareTo(b.nameLen);
                if (cmp != 0) return cmp;
                return a.idx.CompareTo(b.idx);
            });
        }

        private enum MatchType
        {
            None,
            FirstWord,      // Match at start of name, or first word of name
            OtherWord,      // Match on a later word within the name
            Description     // Match only in content/annotation portion (cost, description, hints)
        }

        /// <summary>
        /// Determines what type of match (if any) exists between search and label.
        ///
        /// The label is split at the first ": " or ". " boundary into a name portion
        /// and a content portion. Name tiers (FirstWord/OtherWord) only consider the
        /// name portion with any parenthetical hints stripped, so descriptions that
        /// happen to contain the search term don't pollute the name tiers.
        /// Description tier is the fallback for anything that matches somewhere in
        /// the full label but not in the name portion.
        ///
        /// Both sides are normalized via <see cref="TextNormalization.RemoveDiacritics"/>
        /// so a French user typing "cafe" matches "café", a German user typing "strasse"
        /// matches "Straße", etc. Mirrors OniAccess's accent-insensitive matching.
        /// </summary>
        private MatchType GetMatchType(string search, string label)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(label))
                return MatchType.None;

            string searchLower = TextNormalization.RemoveDiacritics(search.ToLowerInvariant());
            string labelLower = TextNormalization.RemoveDiacritics(label.ToLowerInvariant().Trim());

            int boundary = FindNameBoundary(labelLower);
            string namePortion = boundary >= 0 ? labelLower.Substring(0, boundary) : labelLower;
            string nameText = StripParentheticalContent(namePortion);

            if (nameText.StartsWith(searchLower))
                return MatchType.FirstWord;

            string[] nameWords = nameText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (nameWords.Length > 0 && nameWords[0].StartsWith(searchLower))
                return MatchType.FirstWord;

            for (int i = 1; i < nameWords.Length; i++)
            {
                if (nameWords[i].StartsWith(searchLower))
                    return MatchType.OtherWord;
            }

            // Anything left (cost, description, parenthetical hints) falls to the
            // Description tier if any word in the full label starts with the search.
            string[] allWords = labelLower.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string w in allWords)
            {
                if (w.StartsWith(searchLower))
                    return MatchType.Description;
            }

            return MatchType.None;
        }

        /// <summary>
        /// Gets the next match after the current index, wrapping around if needed.
        /// </summary>
        /// <param name="currentIndex">The current selected index</param>
        /// <returns>The next matching index, or -1 if no matches</returns>
        public int GetNextMatch(int currentIndex)
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }

            // Find current position in matches
            int pos = matchingIndices.IndexOf(currentIndex);

            if (pos >= 0)
            {
                // Move to next match, wrapping around
                currentMatchIndex = (pos + 1) % matchingIndices.Count;
            }
            else
            {
                // Current index not in matches, find next match after current index
                currentMatchIndex = 0;
                for (int i = 0; i < matchingIndices.Count; i++)
                {
                    if (matchingIndices[i] > currentIndex)
                    {
                        currentMatchIndex = i;
                        break;
                    }
                }
            }

            return matchingIndices[currentMatchIndex];
        }

        /// <summary>
        /// Gets the previous match before the current index, wrapping around if needed.
        /// </summary>
        /// <param name="currentIndex">The current selected index</param>
        /// <returns>The previous matching index, or -1 if no matches</returns>
        public int GetPreviousMatch(int currentIndex)
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }

            // Find current position in matches
            int pos = matchingIndices.IndexOf(currentIndex);

            if (pos >= 0)
            {
                // Move to previous match, wrapping around
                currentMatchIndex = (pos - 1 + matchingIndices.Count) % matchingIndices.Count;
            }
            else
            {
                // Current index not in matches, find previous match before current index
                currentMatchIndex = matchingIndices.Count - 1;
                for (int i = matchingIndices.Count - 1; i >= 0; i--)
                {
                    if (matchingIndices[i] < currentIndex)
                    {
                        currentMatchIndex = i;
                        break;
                    }
                }
            }

            return matchingIndices[currentMatchIndex];
        }

        /// <summary>
        /// Gets the first match (for Home during an active search), updating the tracked
        /// match position. Returns the matching list index, or -1 if there are no matches.
        /// </summary>
        public int GetFirstMatch()
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }
            currentMatchIndex = 0;
            return matchingIndices[0];
        }

        /// <summary>
        /// Gets the last match (for End during an active search), updating the tracked
        /// match position. Returns the matching list index, or -1 if there are no matches.
        /// </summary>
        public int GetLastMatch()
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }
            currentMatchIndex = matchingIndices.Count - 1;
            return matchingIndices[currentMatchIndex];
        }

        /// <summary>
        /// Strips content inside parentheses from a string.
        /// Example: "Sleeping spot (description here)" -> "Sleeping spot"
        /// </summary>
        private static string StripParentheticalContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove all content between parentheses (including nested)
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
                    if (depth > 0) depth--;
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
